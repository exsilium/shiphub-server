﻿namespace RealArtists.ShipHub.Mail {
  using System;
  using System.IO;
  using System.Net;
  using System.Net.Http;
  using System.Net.Mail;
  using System.Net.Mime;
  using System.Text;
  using System.Threading.Tasks;
  using Common;
  using Models;
  using PdfSharp.Drawing;
  using PdfSharp.Pdf;
  using PdfSharp.Pdf.IO;
  using Views;

  public interface IShipHubMailer {
    Task CancellationScheduled(CancellationScheduledMailMessage model);
    Task CardExpiryReminder(CardExpiryReminderMailMessage model);
    Task PaymentFailed(PaymentFailedMailMessage model);
    Task PaymentRefunded(PaymentRefundedMailMessage model);
    Task PaymentSucceededPersonal(PaymentSucceededPersonalMailMessage model);
    Task PaymentSucceededOrganization(PaymentSucceededOrganizationMailMessage model);
    Task PurchasePersonal(PurchasePersonalMailMessage model);
    Task PurchaseOrganization(PurchaseOrganizationMailMessage model);
  }

  public class ShipHubMailer : IShipHubMailer {
    public bool IncludeHtmlView { get; set; } = true;

    private static readonly Lazy<string> _BaseDirectory = new Lazy<string>(() => {
      var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
      var binDir = new DirectoryInfo(Path.Combine(dir.FullName, "bin"));
      if (binDir.Exists) {
        return binDir.FullName;
      } else {
        return dir.FullName;
      }
    });
    private static string BaseDirectory => _BaseDirectory.Value;

    private static HttpClient _HttpClient { get; } = new HttpClient(HttpUtilities.CreateDefaultHandler(), true);

    private async Task SendMailMessage<T>(
      ShipHubTemplateBase<T> htmlTemplate,
      ShipHubTemplateBase<T> plainTemplate,
      string subject) where T : MailMessageBase {

      var smtpPassword = ShipHubCloudConfiguration.Instance.SmtpPassword;
      if (smtpPassword.IsNullOrWhiteSpace()) {
        Log.Info("SmtpPassword unset so will not send email.");
        return;
      }

      // MailMessage.Dispose() takes care of views and attchments for us. Yay!

      using (var client = new SmtpClient("smtp.mailgun.org", 587))
      using (var message = new MailMessage()) {
        client.Credentials = new NetworkCredential("shiphub@email.realartists.com", smtpPassword);

        message.From = new MailAddress("support@realartists.com", "Ship");
        message.To.Add(new MailAddress(htmlTemplate.Model.ToAddress, htmlTemplate.Model.ToName));
        message.Bcc.Add(new MailAddress("billing-emails@realartists.com")); // So we can monitor production billing emails.
        message.Subject = subject;
        message.Body = plainTemplate.TransformText();

        // Special behavior if NOT live
        if (ShipHubCloudConfiguration.Instance.ApiHostName != "hub.realartists.com") {
          message.Bcc.Clear(); // Too noisy.
          // So we never have to wonder where an odd email came from.
          message.Subject = $"[{ShipHubCloudConfiguration.Instance.ApiHostName}] {message.Subject}";
        }

        if (IncludeHtmlView) {
          // Let's just use the entire plain text version as the pre-header for now.
          // We don't need to do anything more clever.  Also, it's important that
          // pre-header text be sufficiently long so that the <img> tag's alt text and
          // the href URL don't leak into the pre-header.  The plain text version is long
          // enough for this.
          plainTemplate.Clear();
          plainTemplate.SkipHeaderFooter = true;
          var preheader = plainTemplate.TransformText().Trim();

          htmlTemplate.PreHeader = preheader;
          var html = htmlTemplate.TransformText();

          // Inline CSS is compatible with more mail clients.
          using (var premailer = new PreMailer.Net.PreMailer(html)) {
            html = premailer.MoveCssInline(removeComments: true).Html;
          }

          var htmlView = AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html);
          var linkedResource = new LinkedResource(Path.Combine(BaseDirectory, "ShipLogo.png"), "image/png") {
            ContentId = "ShipLogo.png"
          };
          htmlView.LinkedResources.Add(linkedResource);
          message.AlternateViews.Add(htmlView);
        }

        if (htmlTemplate.Model is IPdfAttachment attachmentInfo && !string.IsNullOrWhiteSpace(attachmentInfo.AttachmentUrl)) {
          var attachment = await CreatePdfAttachment(attachmentInfo);
          if (attachment != null) { // Best effort only
            message.Attachments.Add(attachment);
          }
        }

        await client.SendMailAsync(message);
      }
    }

    private async Task<Attachment> CreatePdfAttachment(IPdfAttachment attachmentInfo) {
      Attachment result = null;

      try {
        // Download the link
        var response = await _HttpClient.GetAsync(attachmentInfo.AttachmentUrl);
        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024);

        // Add a page to the attachment so Mail.app won't inline the preview.
        using (var stream = await response.Content.ReadAsStreamAsync())
        using (var sourceDoc = PdfReader.Open(stream, PdfDocumentOpenMode.Import))
        using (var newDoc = new PdfDocument()) {
          foreach (var page in sourceDoc.Pages) {
            newDoc.AddPage(page);
          }

          var firstPage = sourceDoc.Pages[0];
          var newPage = newDoc.AddPage();
          newPage.Height = firstPage.Height;
          newPage.Width = firstPage.Width;
          using (var gfx = XGraphics.FromPdfPage(newPage)) {
            gfx.DrawString(
              "[This page intentionally left blank.]",
              new XFont("Verdana", 20),
              XBrushes.Black,
              new XRect(0, 0, newPage.Width, newPage.Height),
              XStringFormats.Center);
          }

          var ms = new MemoryStream();
          newDoc.Save(ms, false);
          ms.Position = 0;

          result = new Attachment(ms, attachmentInfo.AttachmentName, MediaTypeNames.Application.Pdf);
        }
      } catch (Exception e) {
        e.Report($"Unable to create attachment: {attachmentInfo.AttachmentUrl}");
      }

      return result;
    }

    public Task CancellationScheduled(CancellationScheduledMailMessage model) {
      return SendMailMessage(
        new CancellationScheduledHtml() { Model = model },
        new CancellationScheduledPlain() { Model = model },
        $"Cancellation for {model.GitHubUserName}");
    }

    public Task CardExpiryReminder(CardExpiryReminderMailMessage model) {
      return SendMailMessage(
        new CardExpiryReminderHtml() { Model = model },
        new CardExpiryReminderPlain() { Model = model },
        $"Card expiration for {model.GitHubUserName}");
    }

    public async Task PaymentFailed(PaymentFailedMailMessage model) {
      await SendMailMessage(
        new PaymentFailedHtml() { Model = model },
        new PaymentFailedPlain() { Model = model },
        $"Payment failed for {model.GitHubUserName}");
    }

    public async Task PaymentRefunded(PaymentRefundedMailMessage model) {
      await SendMailMessage(
        new PaymentRefundedHtml() { Model = model },
        new PaymentRefundedPlain() { Model = model },
        $"Payment refunded for {model.GitHubUserName}");
    }

    public async Task PurchasePersonal(PurchasePersonalMailMessage model) {
      await SendMailMessage(
        new PurchasePersonalHtml() { Model = model },
        new PurchasePersonalPlain() { Model = model },
        $"Ship subscription for {model.GitHubUserName}");
    }

    public async Task PurchaseOrganization(PurchaseOrganizationMailMessage model) {
      await SendMailMessage(
        new PurchaseOrganizationHtml() { Model = model },
        new PurchaseOrganizationPlain() { Model = model },
        $"Ship subscription for {model.GitHubUserName}");
    }

    public async Task PaymentSucceededPersonal(PaymentSucceededPersonalMailMessage model) {
      await SendMailMessage(
        new PaymentSucceededPersonalHtml() { Model = model },
        new PaymentSucceededPersonalPlain() { Model = model },
        $"Payment receipt for {model.GitHubUserName}");
    }

    public async Task PaymentSucceededOrganization(PaymentSucceededOrganizationMailMessage model) {
      await SendMailMessage(
        new PaymentSucceededOrganizationHtml() { Model = model },
        new PaymentSucceededOrganizationPlain() { Model = model },
        $"Payment receipt for {model.GitHubUserName}");
    }
  }
}
