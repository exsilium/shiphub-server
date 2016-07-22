﻿CREATE TABLE [dbo].[Accounts] (
  [Id]                     BIGINT         NOT NULL,
  [Type]                   NVARCHAR(4)    NOT NULL,
  [Login]                  NVARCHAR(255)  NOT NULL,
  [Date]                   DATETIMEOFFSET NOT NULL,
  [MetaDataId]             BIGINT         NULL,
  [RepositoryMetaDataId]   BIGINT         NULL,
  [OrganizationMetaDataId] BIGINT         NULL,
  [Token]                  NVARCHAR(64)   NULL,
  [Scopes]                 NVARCHAR(255)  NOT NULL,
  [RateLimit]              INT            NOT NULL,
  [RateLimitRemaining]     INT            NOT NULL,
  [RateLimitReset]         DATETIMEOFFSET NOT NULL,
  CONSTRAINT [PK_Accounts] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Accounts_MetaDataId_GitHubMetaData_Id] FOREIGN KEY ([MetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
  CONSTRAINT [FK_Accounts_RepositoryMetaDataId_GitHubMetaData_Id] FOREIGN KEY ([RepositoryMetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
  CONSTRAINT [FK_Accounts_OrganizationMetaDataId_GitHubMetaData_Id] FOREIGN KEY ([OrganizationMetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_Accounts_Type] ON [dbo].[Accounts]([Type])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_Login] ON [dbo].[Accounts]([Login])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_MetaDataId]
  ON [dbo].[Accounts]([MetaDataId])
  WHERE ([MetaDataId] IS NOT NULL)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_RepositoryMetaDataId]
  ON [dbo].[Accounts]([RepositoryMetaDataId])
  WHERE ([RepositoryMetaDataId] IS NOT NULL)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_OrganizationMetaDataId]
  ON [dbo].[Accounts]([OrganizationMetaDataId])
  WHERE ([OrganizationMetaDataId] IS NOT NULL)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_Token]
  ON [dbo].[Accounts]([Token])
  WHERE ([Token] IS NOT NULL)
GO
