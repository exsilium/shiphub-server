﻿namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using AutoMapper;
  using Newtonsoft.Json;
  using Types;
  using g = GitHub.Models;

  public class GitHubToDataModelProfile : Profile {
    public GitHubToDataModelProfile() {
      CreateMap<g.Account, Account>(MemberList.Destination);

      // Table Types
      CreateMap<g.Account, AccountTableType>(MemberList.Destination)
        .ForMember(x => x.Type, o => o.ResolveUsing(x => x.Type == g.GitHubAccountType.User ? Account.UserType : Account.OrganizationType));

      CreateMap<g.Comment, CommentTableType>(MemberList.Destination)
        .BeforeMap((from, to) => {
          if (from.IssueNumber == null) {
            throw new InvalidOperationException("Only issue comments are supported.");
          }
        }).ForMember(x => x.Reactions, o => o.ResolveUsing(x => x.Reactions.SerializeObject(Formatting.None)));

      CreateMap<g.Issue, IssueTableType>(MemberList.Destination)
        .ForMember(x => x.Reactions, o => o.ResolveUsing(x => x.Reactions.SerializeObject(Formatting.None)));

      CreateMap<g.IssueEvent, IssueEventTableType>(MemberList.Destination);

      CreateMap<g.Milestone, MilestoneTableType>(MemberList.Destination);

      CreateMap<g.Repository, RepositoryTableType>(MemberList.Destination)
        .ForMember(x => x.AccountId, o => o.MapFrom(x => x.Owner.Id));
    }
  }
}
