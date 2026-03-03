/* 0) Nos ubicamos en master para crear el Login a nivel servidor */
USE [master];
GO

CREATE LOGIN [biofx_sql] 
WITH PASSWORD = 'B10FX#',
     CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
GO

/* 1) Nos ubicamos en tu base de datos y creamos el usuario */
USE [BioFXBD];
GO

CREATE USER [biofx_sql] FOR LOGIN [biofx_sql] WITH DEFAULT_SCHEMA = [dbo];
GO

/* 2) Creamos un rol específico para la tabla Producto (Opcional pero recomendado basado en tu ejemplo) */
CREATE ROLE [rol_producto_rw_nodlt];
GO

-- Permiso para conectarse a la base de datos
GRANT CONNECT TO [biofx_sql];
GO

/* 3) Asignamos los permisos granulares a la TABLA ESPECÍFICA, no al esquema */
GRANT SELECT, INSERT, UPDATE ON [dbo].[Producto] TO [rol_producto_rw_nodlt];
DENY  DELETE ON [dbo].[Producto] TO [rol_producto_rw_nodlt];
GO

/* 4) Asignamos el rol al usuario nuevo */
ALTER ROLE [rol_producto_rw_nodlt] ADD MEMBER [biofx_sql];
GO