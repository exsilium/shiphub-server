﻿CREATE USER [ReadOnly]
  WITH PASSWORD = 'PRjAmPIx584ld5HuuG11'
GO

GRANT CONNECT TO [ReadOnly]
GO

GRANT VIEW DATABASE STATE TO [ReadOnly]
GO

GRANT VIEW DEFINITION TO [ReadOnly]
GO
