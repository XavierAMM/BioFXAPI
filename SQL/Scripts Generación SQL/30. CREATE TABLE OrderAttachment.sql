CREATE TABLE [dbo].[OrderAttachment](
    [Id]               INT IDENTITY(1,1) NOT NULL,
    [FileName]         NVARCHAR(255) NOT NULL,
    [ContentType]      NVARCHAR(100) NOT NULL,
    [FileSize]         BIGINT NOT NULL,
    [StorageKey]       NVARCHAR(500) NOT NULL, -- clave/“key” en S3 (ej: invoices/{guid}.pdf)
    [Tipo]             NVARCHAR(50) NOT NULL,  -- ej: 'FACTURA'
    [Activo]           BIT NOT NULL CONSTRAINT [DF_OrderAttachment_Activo] DEFAULT (1),
    [CreadoEl]         DATETIME2(7) NOT NULL CONSTRAINT [DF_OrderAttachment_CreadoEl] DEFAULT (SYSUTCDATETIME()),
    [ActualizadoEl]    DATETIME2(7) NOT NULL CONSTRAINT [DF_OrderAttachment_ActualizadoEl] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_OrderAttachment] PRIMARY KEY CLUSTERED ([Id] ASC)
);
