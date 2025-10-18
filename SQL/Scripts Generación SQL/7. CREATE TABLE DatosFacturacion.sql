CREATE TABLE [dbo].[DatosFacturacion](
    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [UsuarioId] [int] NOT NULL,
    [Nombre_Razon_Social] [nvarchar](255) NULL,
    [RUC_Cedula] [nvarchar](20) NULL,
    [Direccion] [nvarchar](500) NULL,
    [Telefono] [nvarchar](10) NULL,
    [Email] [nvarchar](255) NULL,
    [Activo] [bit] NOT NULL DEFAULT 1,
    [CreadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [ActualizadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_DatosFacturacion_Usuario] FOREIGN KEY([UsuarioId]) 
        REFERENCES [dbo].[Usuario] ([id]) ON DELETE CASCADE
);

CREATE NONCLUSTERED INDEX [IX_DatosFacturacion_UsuarioId] 
ON [dbo].[DatosFacturacion] ([UsuarioId]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_DatosFacturacion_UsuarioId_Unico] 
ON [dbo].[DatosFacturacion] ([UsuarioId]) 
WHERE [Activo] = 1;