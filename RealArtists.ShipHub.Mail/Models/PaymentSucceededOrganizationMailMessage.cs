﻿namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PaymentSucceededOrganizationMailMessage : MailMessageBase {
    public double AmountPaid { get; set; }
    public string LastCardDigits { get; set; }
    public byte[] InvoicePdfBytes { get; set; }
    public DateTimeOffset InvoiceDate { get; set; }
    public int PreviousMonthActiveUsersCount { get; set; }
    public string[] PreviousMonthActiveUsersSample { get; set; }
    public DateTimeOffset PreviousMonthStart { get; set; }
    public DateTimeOffset ServiceThroughDate { get; set; }
  }
}