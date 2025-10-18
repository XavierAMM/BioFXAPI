-- ============================================================
-- FULL idempotent setup script for 'public_user' on BioFXBD
-- Run this as a sysadmin (e.g. sa). Change the password below.
-- ============================================================

-- =========================
-- 1) Create server login (master)
-- =========================
USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'public_user')
BEGIN
    CREATE LOGIN [public_user] WITH PASSWORD = N'Publ1c_us3r#';
END
ELSE
BEGIN
    -- Optionally, you could ALTER LOGIN to change password or settings if needed.
    PRINT N'Login [public_user] already exists.';
END
GO

-- =========================
-- 2) Create / map database user, grant CONNECT and CRUD for existing tables (BioFXBD)
-- =========================
USE [BioFXBD];
GO

-- Create or remap the database user to the login
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'public_user')
BEGIN
    CREATE USER [public_user] FOR LOGIN [public_user];
    PRINT N'Database user [public_user] created.';
END
ELSE
BEGIN
    -- Remap in case it's orphaned
    BEGIN TRY
        ALTER USER [public_user] WITH LOGIN = [public_user];
        PRINT N'Database user [public_user] mapped to login [public_user].';
    END TRY
    BEGIN CATCH
        PRINT 'Warning: ALTER USER failed: ' + ERROR_MESSAGE();
    END CATCH
END
GO

-- Ensure the user can connect
GRANT CONNECT TO [public_user];
GO

-- Grant CRUD on all existing user tables (idempotent)
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql + 'GRANT SELECT, INSERT, UPDATE, DELETE ON '
    + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name)
    + ' TO [public_user];' + CHAR(13)
FROM sys.tables;

IF LEN(@sql) > 0
    EXEC sp_executesql @sql;
GO

-- (Optional additional safety) Grant CRUD on the dbo schema so typical tables are covered
-- This ensures permissions on current + future dbo schema objects as well.
-- If you prefer only the trigger approach, you can remove this block.
BEGIN TRY
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [public_user];
END TRY
BEGIN CATCH
    PRINT 'Note: Grant on SCHEMA::dbo failed or already present: ' + ERROR_MESSAGE();
END CATCH;
GO

-- =========================
-- 3) Remove any older duplicates of the trigger, then create ONE DDL trigger
--    The trigger uses EVENTDATA() to grant CRUD on newly created tables.
-- =========================
-- Drop existing trigger named GrantCRUDOnNewTables if present
IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'GrantCRUDOnNewTables')
BEGIN
    DROP TRIGGER GrantCRUDOnNewTables ON DATABASE;
    PRINT 'Dropped existing GrantCRUDOnNewTables trigger.';
END
GO

CREATE TRIGGER GrantCRUDOnNewTables
ON DATABASE
AFTER CREATE_TABLE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @evt XML = EVENTDATA();
    DECLARE @schema SYSNAME = @evt.value('(/EVENT_INSTANCE/SchemaName)[1]', 'SYSNAME');
    DECLARE @tbl SYSNAME    = @evt.value('(/EVENT_INSTANCE/ObjectName)[1]', 'SYSNAME');

    IF @tbl IS NULL OR @schema IS NULL
    BEGIN
        RETURN;
    END

    DECLARE @fullname NVARCHAR(510) = QUOTENAME(@schema) + N'.' + QUOTENAME(@tbl);

    DECLARE @cmd NVARCHAR(MAX) = N'GRANT SELECT, INSERT, UPDATE, DELETE ON ' + @fullname + N' TO [public_user];';

    BEGIN TRY
        EXEC sp_executesql @cmd;
    END TRY
    BEGIN CATCH
        -- If anything goes wrong, write to the SQL error log (or ignore silently)
        DECLARE @err NVARCHAR(MAX) = ERROR_MESSAGE();
        RAISERROR('GrantCRUDOnNewTables trigger error: %s', 10, 1, @err) WITH NOWAIT;
    END CATCH
END;
GO

-- =========================
-- 4) Set login default DB and apply server-level DENY (must run in master)
-- =========================
USE master;
GO

-- Set the login default DB to BioFXBD (so connections default there)
ALTER LOGIN [public_user] WITH DEFAULT_DATABASE = [BioFXBD];
GO

-- Deny high-level server permissions (server-scope)
-- These statements are safe to run even if previously denied
BEGIN TRY
    DENY ALTER ANY DATABASE TO [public_user];
    DENY ALTER ANY LOGIN TO [public_user];
END TRY
BEGIN CATCH
    PRINT 'Note: DENY server-level permissions error: ' + ERROR_MESSAGE();
END CATCH
GO

-- =========================
-- 5) Deny database-level CONTROL (run in BioFXBD)
-- =========================
USE [BioFXBD];
GO

BEGIN TRY
    DENY CONTROL ON DATABASE::[BioFXBD] TO [public_user];
END TRY
BEGIN CATCH
    PRINT 'Note: DENY CONTROL failed or already applied: ' + ERROR_MESSAGE();
END CATCH
GO

-- =========================
-- 6) Final check prints (informational)
-- =========================
PRINT 'Setup completed. Verify by logging in as [public_user] and running:';
PRINT 'USE BioFXBD; SELECT TOP(5) * FROM <a_table>;';

-- End of script
GO
