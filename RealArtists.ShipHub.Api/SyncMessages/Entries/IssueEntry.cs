﻿namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System;
  using System.Collections.Generic;

  public class IssueEntry : SyncEntity {
    public long Identifier { get; set; }
    public long User { get; set; }
    public long Repository { get; set; }
    public int Number { get; set; }
    public string State { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public long? Assignee { get; set; }
    public long? Milestone { get; set; }
    public bool Locked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public long? ClosedBy { get; set; }
    public Reactions Reactions { get; set; }

    public IEnumerable<Label> Labels { get; set; }
  }
}