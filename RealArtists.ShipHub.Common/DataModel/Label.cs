﻿namespace RealArtists.ShipHub.Common.DataModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.Diagnostics.CodeAnalysis;

  public class Label {
    public long Id { get; set; }

    [Required]
    [StringLength(6)]
    public string Color { get; set; }

    [Required]
    [StringLength(500)]
    public string Name { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> Issues { get; set; } = new HashSet<Issue>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Repository> Repositories { get; set; } = new HashSet<Repository>();
  }
}