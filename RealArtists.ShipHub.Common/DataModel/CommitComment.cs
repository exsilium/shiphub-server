﻿namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using Types;

  public class CommitComment {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    public long RepositoryId { get; set; }

    public long UserId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CommitId { get; set; }

    public string Path { get; set; }

    public long? Position { get; set; }

    public long? Line { get; set; }

    [Required]
    public string Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string MetadataJson {
      get => Metadata.SerializeObject();
      set => Metadata = value.DeserializeObject<GitHubMetadata>();
    }

    [NotMapped]
    public GitHubMetadata Metadata { get; set; }

    public string ReactionMetadataJson {
      get => ReactionMetadata.SerializeObject();
      set => ReactionMetadata = value.DeserializeObject<GitHubMetadata>();
    }

    [NotMapped]
    public GitHubMetadata ReactionMetadata { get; set; }

    public virtual Repository Repository { get; set; }

    public virtual Account User { get; set; }
  }
}
