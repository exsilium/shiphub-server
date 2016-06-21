﻿CREATE TYPE [dbo].[IssueEventTableType] AS TABLE (
  [Id]            BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [IssueId]       BIGINT         NOT NULL,
  [ActorId]       BIGINT         NOT NULL,
  [CommitId]      NVARCHAR(40)   NULL,
  [Event]         NVARCHAR(64)   NOT NULL,
  [CreatedAt]     DATETIMEOFFSET NOT NULL,
  [AssigneeId]    BIGINT         NULL,
  [ExtensionData] NVARCHAR(MAX)  NOT NULL
)
