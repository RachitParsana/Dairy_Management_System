-- ====================================================================================
-- Script: 14_NotificationReadSyncAndProfileManagement.sql
-- Description:
--   1. Notification Read Tracking Synchronization:
--      - Updates dbo.usp_Notifications_GetForUser to auto-resolve DeliveryBoyId &
--        PrimaryRouteId for DeliveryBoy role and include NotificationPopupSeen state.
--      - Updates dbo.usp_Notifications_MarkPopupSeen to also record a read receipt
--        in dbo.NotificationAcknowledgements so Admin Console reflects 'Acknowledged'.
--      - Updates dbo.usp_Notifications_GetAdminOverview to return AcknowledgementCount,
--        AcknowledgedAt, and AcknowledgedByNames.
--      - Adds dbo.usp_Notifications_MarkAllReadForUser for one-click Mark All Read.
--      - Synchronizes any existing NotificationPopupSeen rows into NotificationAcknowledgements.
--   2. Profile & Password Management (Least-Privilege Ownership Chaining):
--      - Adds dbo.usp_Users_GetProfileById, dbo.usp_Users_GetAuthById,
--        dbo.usp_Users_GetAuthByDeliveryBoyId, dbo.usp_Users_UpdateOwnProfile,
--        and dbo.usp_Users_UpdatePasswordHash with EXECUTE grants to [db_dms_app].
--
-- Target DB: DairyManagementDb (Idempotent deployment)
-- ====================================================================================

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
BEGIN
    DECLARE @SysDbMsg NVARCHAR(512) = N'Deployment halted: Cannot run against system database [' + DB_NAME() + N'].';
    THROW 50000, @SysDbMsg, 1;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'db_dms_app') IS NULL
BEGIN
    DECLARE @RoleMsg NVARCHAR(512) = N'Deployment halted: Role [db_dms_app] does not exist in [' + DB_NAME() + N'].';
    THROW 50001, @RoleMsg, 1;
END;
GO

PRINT '====================================================================================';
PRINT 'Deploying 14_NotificationReadSyncAndProfileManagement.sql on [' + DB_NAME() + ']...';
PRINT '====================================================================================';
GO

-- ------------------------------------------------------------------------------------
-- 1. Fix dbo.usp_Notifications_GetForUser (Auto-resolve DeliveryBoy & Route + Seen Sync)
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_GetForUser]
    @UserId INT,
    @Role NVARCHAR(20),
    @DeliveryBoyId INT = NULL,
    @RouteId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF (@Role = 'DeliveryBoy' AND (@DeliveryBoyId IS NULL OR @RouteId IS NULL))
    BEGIN
        SELECT
            @DeliveryBoyId = ISNULL(@DeliveryBoyId, db.[DeliveryBoyId]),
            @RouteId = ISNULL(@RouteId, db.[PrimaryRouteId])
        FROM [dbo].[DeliveryBoys] db
        WHERE db.[UserId] = @UserId AND db.[IsActive] = 1;
    END

    SELECT
        n.[NotificationId],
        n.[Title],
        n.[Message],
        n.[NotificationType],
        n.[TargetRole],
        n.[TargetRouteId],
        r.[RouteName] AS TargetRouteName,
        n.[TargetDeliveryBoyId],
        n.[CustomerId],
        n.[BillId],
        n.[CustomerNameSnapshot],
        n.[UpdatedBillAmount],
        n.[NewTotalOutstandingBalance],
        n.[BillingMonthYear],
        n.[PendingMonthsCount],
        n.[CreatedAt],
        u.[FullName] AS CreatedByName,
        CASE WHEN na.[AcknowledgementId] IS NOT NULL OR nps.[PopupSeenId] IS NOT NULL THEN 1 ELSE 0 END AS IsAcknowledged,
        COALESCE(na.[AcknowledgedAt], nps.[SeenAt]) AS AcknowledgedAt
    FROM [dbo].[Notifications] n
    LEFT JOIN [dbo].[Routes] r ON n.[TargetRouteId] = r.[RouteId]
    LEFT JOIN [dbo].[Users] u ON n.[CreatedBy] = u.[UserId]
    LEFT JOIN [dbo].[NotificationAcknowledgements] na
           ON n.[NotificationId] = na.[NotificationId] AND na.[UserId] = @UserId
    LEFT JOIN [dbo].[NotificationPopupSeen] nps
           ON n.[NotificationId] = nps.[NotificationId] AND nps.[UserId] = @UserId
    WHERE (n.[TargetRole] = 'All' OR n.[TargetRole] = @Role)
      AND (
          (
              @Role = 'DeliveryBoy'
              AND (
                  n.[TargetDeliveryBoyId] = @DeliveryBoyId
                  OR (n.[TargetDeliveryBoyId] IS NULL AND (n.[TargetRouteId] IS NULL OR n.[TargetRouteId] = @RouteId))
              )
          )
          OR
          (
              @Role <> 'DeliveryBoy'
              AND (@RouteId IS NULL OR n.[TargetRouteId] IS NULL OR n.[TargetRouteId] = @RouteId)
              AND (@DeliveryBoyId IS NULL OR n.[TargetDeliveryBoyId] IS NULL OR n.[TargetDeliveryBoyId] = @DeliveryBoyId)
          )
      )
    ORDER BY n.[CreatedAt] DESC, n.[NotificationId] DESC;
END;
GO

GRANT EXECUTE ON [dbo].[usp_Notifications_GetForUser] TO [db_dms_app];
GO

-- ------------------------------------------------------------------------------------
-- 2. Fix dbo.usp_Notifications_MarkPopupSeen (Also records read receipt)
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_MarkPopupSeen]
    @NotificationId BIGINT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[Notifications] WHERE [NotificationId] = @NotificationId)
        THROW 50063, 'Notification not found.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[NotificationPopupSeen]
        WHERE [NotificationId] = @NotificationId AND [UserId] = @UserId
    )
    BEGIN
        INSERT INTO [dbo].[NotificationPopupSeen] ([NotificationId], [UserId], [SeenAt])
        VALUES (@NotificationId, @UserId, SYSUTCDATETIME());
    END;

    -- Also persist read/seen acknowledgement so Admin Notification Console displays Acknowledged status
    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[NotificationAcknowledgements]
        WHERE [NotificationId] = @NotificationId AND [UserId] = @UserId
    )
    BEGIN
        INSERT INTO [dbo].[NotificationAcknowledgements] ([NotificationId], [UserId], [AcknowledgedAt])
        VALUES (@NotificationId, @UserId, SYSUTCDATETIME());
    END;
END;
GO

GRANT EXECUTE ON [dbo].[usp_Notifications_MarkPopupSeen] TO [db_dms_app];
GO

-- ------------------------------------------------------------------------------------
-- 3. New dbo.usp_Notifications_MarkAllReadForUser
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_MarkAllReadForUser]
    @UserId INT,
    @Role NVARCHAR(20),
    @DeliveryBoyId INT = NULL,
    @RouteId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF (@Role = 'DeliveryBoy' AND (@DeliveryBoyId IS NULL OR @RouteId IS NULL))
    BEGIN
        SELECT
            @DeliveryBoyId = ISNULL(@DeliveryBoyId, db.[DeliveryBoyId]),
            @RouteId = ISNULL(@RouteId, db.[PrimaryRouteId])
        FROM [dbo].[DeliveryBoys] db
        WHERE db.[UserId] = @UserId AND db.[IsActive] = 1;
    END

    INSERT INTO [dbo].[NotificationAcknowledgements] ([NotificationId], [UserId], [AcknowledgedAt])
    SELECT n.[NotificationId], @UserId, SYSUTCDATETIME()
    FROM [dbo].[Notifications] n
    WHERE (n.[TargetRole] = 'All' OR n.[TargetRole] = @Role)
      AND (
          (
              @Role = 'DeliveryBoy'
              AND (
                  n.[TargetDeliveryBoyId] = @DeliveryBoyId
                  OR (n.[TargetDeliveryBoyId] IS NULL AND (n.[TargetRouteId] IS NULL OR n.[TargetRouteId] = @RouteId))
              )
          )
          OR
          (
              @Role <> 'DeliveryBoy'
              AND (@RouteId IS NULL OR n.[TargetRouteId] IS NULL OR n.[TargetRouteId] = @RouteId)
              AND (@DeliveryBoyId IS NULL OR n.[TargetDeliveryBoyId] IS NULL OR n.[TargetDeliveryBoyId] = @DeliveryBoyId)
          )
      )
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[NotificationAcknowledgements] na
          WHERE na.[NotificationId] = n.[NotificationId] AND na.[UserId] = @UserId
      );

    INSERT INTO [dbo].[NotificationPopupSeen] ([NotificationId], [UserId], [SeenAt])
    SELECT n.[NotificationId], @UserId, SYSUTCDATETIME()
    FROM [dbo].[Notifications] n
    WHERE n.[NotificationType] = 'BillPublished'
      AND (n.[TargetRole] = 'All' OR n.[TargetRole] = @Role)
      AND (
          n.[TargetDeliveryBoyId] = @DeliveryBoyId
          OR (n.[TargetDeliveryBoyId] IS NULL AND (n.[TargetRouteId] IS NULL OR n.[TargetRouteId] = @RouteId))
      )
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[NotificationPopupSeen] nps
          WHERE nps.[NotificationId] = n.[NotificationId] AND nps.[UserId] = @UserId
      );
END;
GO

GRANT EXECUTE ON [dbo].[usp_Notifications_MarkAllReadForUser] TO [db_dms_app];
GO

-- ------------------------------------------------------------------------------------
-- 4. Fix dbo.usp_Notifications_GetAdminOverview (Include AcknowledgedAt & Readers)
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_GetAdminOverview]
    @Limit INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Limit)
        n.[NotificationId],
        n.[Title],
        n.[Message],
        n.[NotificationType],
        n.[TargetRole],
        r.[RouteName] AS TargetRouteName,
        udb.[FullName] AS TargetDeliveryBoyName,
        n.[CustomerNameSnapshot],
        n.[UpdatedBillAmount],
        n.[BillingMonthYear],
        n.[CreatedAt],
        u.[FullName] AS CreatedByName,
        ISNULL(ack.[AcknowledgementCount], 0) AS AcknowledgementCount,
        ack.[AcknowledgedAt],
        ack.[AcknowledgedByNames]
    FROM [dbo].[Notifications] n
    LEFT JOIN [dbo].[Routes] r ON n.[TargetRouteId] = r.[RouteId]
    LEFT JOIN [dbo].[DeliveryBoys] db ON n.[TargetDeliveryBoyId] = db.[DeliveryBoyId]
    LEFT JOIN [dbo].[Users] udb ON db.[UserId] = udb.[UserId]
    LEFT JOIN [dbo].[Users] u ON n.[CreatedBy] = u.[UserId]
    OUTER APPLY (
        SELECT
            COUNT(1) AS AcknowledgementCount,
            MAX(na.[AcknowledgedAt]) AS AcknowledgedAt,
            STRING_AGG(ua.[FullName], N', ') WITHIN GROUP (ORDER BY na.[AcknowledgedAt] DESC) AS AcknowledgedByNames
        FROM [dbo].[NotificationAcknowledgements] na
        INNER JOIN [dbo].[Users] ua ON na.[UserId] = ua.[UserId]
        WHERE na.[NotificationId] = n.[NotificationId]
    ) ack
    ORDER BY n.[CreatedAt] DESC, n.[NotificationId] DESC;
END;
GO

GRANT EXECUTE ON [dbo].[usp_Notifications_GetAdminOverview] TO [db_dms_app];
GO

-- Sync any previously dismissed popups into NotificationAcknowledgements so prior reads show immediately
INSERT INTO [dbo].[NotificationAcknowledgements] ([NotificationId], [UserId], [AcknowledgedAt])
SELECT nps.[NotificationId], nps.[UserId], nps.[SeenAt]
FROM [dbo].[NotificationPopupSeen] nps
WHERE EXISTS (SELECT 1 FROM [dbo].[Notifications] n WHERE n.[NotificationId] = nps.[NotificationId])
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[NotificationAcknowledgements] na
      WHERE na.[NotificationId] = nps.[NotificationId] AND na.[UserId] = nps.[UserId]
  );
GO

-- ------------------------------------------------------------------------------------
-- 5. Profile & Password Management Stored Procedures (Ownership Chaining under db_dms_app)
-- ------------------------------------------------------------------------------------

-- 5.1 Safe Profile Projection (Excludes PasswordHash)
CREATE OR ALTER PROCEDURE [dbo].[usp_Users_GetProfileById]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.[UserId],
        u.[Username],
        u.[Role],
        u.[FullName],
        u.[Phone],
        u.[IsActive],
        u.[LastLoginAt],
        u.[CreatedAt]
    FROM [dbo].[Users] u
    WHERE u.[UserId] = @UserId;
END;
GO

GRANT EXECUTE ON [dbo].[usp_Users_GetProfileById] TO [db_dms_app];
GO

-- 5.2 Internal Auth Verification Fetch by UserId
CREATE OR ALTER PROCEDURE [dbo].[usp_Users_GetAuthById]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.[UserId],
        u.[Username],
        u.[PasswordHash],
        u.[Role],
        u.[FullName],
        u.[Phone],
        u.[IsActive],
        u.[LastLoginAt]
    FROM [dbo].[Users] u
    WHERE u.[UserId] = @UserId AND u.[IsActive] = 1;
END;
GO

GRANT EXECUTE ON [dbo].[usp_Users_GetAuthById] TO [db_dms_app];
GO

-- 5.3 Internal Auth Verification Fetch by DeliveryBoyId (for Admin password reset)
CREATE OR ALTER PROCEDURE [dbo].[usp_Users_GetAuthByDeliveryBoyId]
    @DeliveryBoyId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.[UserId],
        u.[Username],
        u.[PasswordHash],
        u.[Role],
        u.[FullName],
        u.[Phone],
        u.[IsActive],
        u.[LastLoginAt]
    FROM [dbo].[DeliveryBoys] db
    INNER JOIN [dbo].[Users] u ON db.[UserId] = u.[UserId]
    WHERE db.[DeliveryBoyId] = @DeliveryBoyId;
END;
GO

GRANT EXECUTE ON [dbo].[usp_Users_GetAuthByDeliveryBoyId] TO [db_dms_app];
GO

-- 5.4 Update Own Profile (Admin: FullName, Username, Phone | DeliveryBoy: FullName, Phone, VehicleNumber)
CREATE OR ALTER PROCEDURE [dbo].[usp_Users_UpdateOwnProfile]
    @UserId INT,
    @Username NVARCHAR(50) = NULL,
    @FullName NVARCHAR(100),
    @Phone VARCHAR(15),
    @VehicleNumber NVARCHAR(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @FullName = LTRIM(RTRIM(ISNULL(@FullName, N'')));
    SET @Phone = LTRIM(RTRIM(ISNULL(@Phone, '')));
    SET @Username = NULLIF(LTRIM(RTRIM(@Username)), N'');
    SET @VehicleNumber = NULLIF(LTRIM(RTRIM(@VehicleNumber)), N'');

    IF (LEN(@FullName) = 0)
        THROW 50072, 'Full name is required.', 1;

    IF (LEN(@Phone) = 0)
        THROW 50073, 'Mobile number is required.', 1;

    DECLARE @Role NVARCHAR(20);
    DECLARE @CurrentUsername NVARCHAR(50);
    DECLARE @IsActive BIT;

    SELECT @Role = [Role], @CurrentUsername = [Username], @IsActive = [IsActive]
    FROM [dbo].[Users] WITH (UPDLOCK, HOLDLOCK)
    WHERE [UserId] = @UserId;

    IF (@Role IS NULL OR @IsActive = 0)
        THROW 50074, 'Active user account not found.', 1;

    IF EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Phone] = @Phone AND [UserId] <> @UserId)
        THROW 50070, 'This mobile number is already registered to another user.', 1;

    DECLARE @TargetUsername NVARCHAR(50) = @CurrentUsername;
    IF (@Role = 'Admin' AND @Username IS NOT NULL)
    BEGIN
        IF EXISTS (SELECT 1 FROM [dbo].[Users] WHERE LOWER([Username]) = LOWER(@Username) AND [UserId] <> @UserId)
            THROW 50071, 'This username is already taken by another account.', 1;
        SET @TargetUsername = @Username;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE [dbo].[Users]
        SET [Username] = @TargetUsername,
            [FullName] = @FullName,
            [Phone] = @Phone,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @UserId
        WHERE [UserId] = @UserId;

        IF (@Role = 'DeliveryBoy')
        BEGIN
            UPDATE [dbo].[DeliveryBoys]
            SET [VehicleNumber] = @VehicleNumber,
                [UpdatedAt] = SYSUTCDATETIME(),
                [UpdatedBy] = @UserId
            WHERE [UserId] = @UserId;
        END

        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
        BEGIN
            INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
            VALUES (
                @UserId,
                'UPDATE_OWN_PROFILE',
                'Users',
                CAST(@UserId AS VARCHAR(50)),
                CONCAT('{"Username":"', REPLACE(@TargetUsername, '"', '\"'), '","FullName":"', REPLACE(@FullName, '"', '\"'), '"}'),
                SYSUTCDATETIME()
            );
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON [dbo].[usp_Users_UpdateOwnProfile] TO [db_dms_app];
GO

-- 5.5 Update Password Hash (Self-Service Password Change & Admin DeliveryBoy Password Reset)
CREATE OR ALTER PROCEDURE [dbo].[usp_Users_UpdatePasswordHash]
    @UserId INT,
    @NewPasswordHash NVARCHAR(256),
    @ModifiedByUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF (@NewPasswordHash IS NULL OR LEN(LTRIM(RTRIM(@NewPasswordHash))) = 0)
        THROW 50075, 'Valid cryptographic password hash is required.', 1;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [UserId] = @UserId AND [IsActive] = 1)
        THROW 50074, 'Active target user account not found.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE [dbo].[Users]
        SET [PasswordHash] = @NewPasswordHash,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @ModifiedByUserId
        WHERE [UserId] = @UserId;

        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
        BEGIN
            INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
            VALUES (
                @ModifiedByUserId,
                CASE WHEN @UserId = @ModifiedByUserId THEN 'CHANGE_PASSWORD' ELSE 'ADMIN_RESET_PASSWORD' END,
                'Users',
                CAST(@UserId AS VARCHAR(50)),
                '{"CredentialUpdated":true}',
                SYSUTCDATETIME()
            );
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON [dbo].[usp_Users_UpdatePasswordHash] TO [db_dms_app];
GO

PRINT '====================================================================================';
PRINT '14_NotificationReadSyncAndProfileManagement.sql deployed successfully!';
PRINT '====================================================================================';
GO
