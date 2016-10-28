﻿namespace RealArtists.ShipHub.MailTester {
  using System;
  using System.IO;
  using System.Linq;
  using Mail;

  class Program {
    static void SendEmails(string toAddress, string toName, string githubUsername, bool includeHtmlVersion) {
      var dummyInvoicePdfBytes = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DummyInvoice.pdf"));

      var mailer = new ShipHubMailer();
      mailer.IncludeHtmlView = includeHtmlVersion;

      mailer.PurchasePersonal(
        new Mail.Models.PurchasePersonalMailMessage() {
          GitHubUsername = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          BelongsToOrganization = true,
          WasGivenTrialCredit = true,
          InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
          InvoicePdfBytes = dummyInvoicePdfBytes,
        }).Wait();

      mailer.PurchaseOrganization(
        new Mail.Models.PurchaseOrganizationMailMessage() {
          GitHubUsername = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
          InvoicePdfBytes = dummyInvoicePdfBytes,
        }).Wait();

      mailer.PaymentSucceededPersonal(
        new Mail.Models.PaymentSucceededPersonalMailMessage() {
          GitHubUsername = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
          InvoicePdfBytes = dummyInvoicePdfBytes,
          LastCardDigits = "1234",
          AmountPaid = 9.00,
          ServiceThroughDate = new DateTimeOffset(2016, 06, 01, 0, 0, 0, TimeSpan.Zero),
        }).Wait();

      // Version with <= 5 active users
      mailer.PaymentSucceededOrganization(
        new Mail.Models.PaymentSucceededOrganizationMailMessage() {
          GitHubUsername = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
          InvoicePdfBytes = dummyInvoicePdfBytes,
          LastCardDigits = "1234",
          AmountPaid = 25.00,
          ServiceThroughDate = new DateTimeOffset(2016, 06, 01, 0, 0, 0, TimeSpan.Zero),
          PreviousMonthStart = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero).AddMonths(-1),
          PreviousMonthActiveUsersCount = 4,
          PreviousMonthActiveUsersSample = Enumerable.Range(1, 4).Select(x => "user_" + x).ToArray()
        }).Wait();
      mailer.PaymentSucceededOrganization(
        new Mail.Models.PaymentSucceededOrganizationMailMessage() {
          GitHubUsername = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
          InvoicePdfBytes = dummyInvoicePdfBytes,
          LastCardDigits = "1234",
          AmountPaid = 25.00 + (9 * 25),
          ServiceThroughDate = new DateTimeOffset(2016, 06, 01, 0, 0, 0, TimeSpan.Zero),
          PreviousMonthStart = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero).AddMonths(-1),
          PreviousMonthActiveUsersCount = 25,
          PreviousMonthActiveUsersSample = Enumerable.Range(1, 20).Select(x => "user_" + x).ToArray(),
        }).Wait();
    }

    static void Main(string[] args) {

      var toAddresses = new[] {
        "fred@realartists.com",
        "fpotter@gmail.com",
      };
      var toName = "Fred Potter";
      var githubUsername = "fpotter";

      foreach (var toAddress in toAddresses) {
        SendEmails(toAddress, toName, githubUsername, includeHtmlVersion: true);
        SendEmails(toAddress, toName, githubUsername, includeHtmlVersion: false);
      }
    }
  }
}