BEGIN TRANSACTION
/* 0) Login a nivel servidor */
USE [master];
GO
CREATE LOGIN [api_app] 
WITH PASSWORD = '#Cliente2631',
     CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
GO

/* 1) Usuario dentro de la BD */
USE [BioFXBD];
GO
CREATE USER [api_app] FOR LOGIN [api_app] WITH DEFAULT_SCHEMA = [dbo];
GO

/* 2) Rol con permisos granulares en el esquema dbo */
CREATE ROLE [rw_no_delete];
GO
GRANT CONNECT TO [rw_no_delete];
GRANT SELECT, INSERT, UPDATE ON SCHEMA::[dbo] TO [rw_no_delete];
GRANT EXECUTE ON SCHEMA::[dbo] TO [rw_no_delete];   -- por si tu API usa SPs
DENY  DELETE ON SCHEMA::[dbo] TO [rw_no_delete];
GO

/* 3) Asignar el rol al usuario */
ALTER ROLE [rw_no_delete] ADD MEMBER [api_app];
GO


COMMIT