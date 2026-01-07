Use BioFXBD;

BEGIN TRANSACTION;

IF OBJECT_ID('dbo.EmailVerificationTokens', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmailVerificationTokens](
        [Id]              INT IDENTITY(1,1) NOT NULL,
        [UsuarioId]       INT NOT NULL,

        -- Guardar hash del token (idealmente SHA-256 => 32 bytes)
        [TokenHash]       VARBINARY(32) NOT NULL,

        -- Auditoría / estado
        [Activo]          BIT NOT NULL,
        [CreadoEl]        DATETIME2(7) NOT NULL,
        [ActualizadoEl]   DATETIME2(7) NOT NULL,

        -- Control del ciclo de vida del token
        [ExpiraEl]        DATETIME2(7) NOT NULL,
        [UsadoEl]         DATETIME2(7) NULL,
        [RevocadoEl]      DATETIME2(7) NULL,

        -- Auditoría adicional útil (opcional)
        [EmailEnviadoA]   NVARCHAR(255) NOT NULL,
        [IpCreacion]      NVARCHAR(45) NULL,

        CONSTRAINT [PK_EmailVerificationTokens] PRIMARY KEY CLUSTERED ([Id] ASC),

        CONSTRAINT [FK_EmailVerificationTokens_Usuario]
            FOREIGN KEY([UsuarioId]) REFERENCES [dbo].[Usuario]([id])
            -- Recomendación: NO cascade delete (mejor mantener historial). Si prefieres limpieza automática, cambia a ON DELETE CASCADE.
    );

    -- Defaults (alineado con tu uso de GETUTCDATE en otras tablas)
    ALTER TABLE [dbo].[EmailVerificationTokens] ADD CONSTRAINT [DF_EmailVerificationTokens_Activo]
        DEFAULT ((1)) FOR [Activo];

    ALTER TABLE [dbo].[EmailVerificationTokens] ADD CONSTRAINT [DF_EmailVerificationTokens_CreadoEl]
        DEFAULT (GETUTCDATE()) FOR [CreadoEl];

    ALTER TABLE [dbo].[EmailVerificationTokens] ADD CONSTRAINT [DF_EmailVerificationTokens_ActualizadoEl]
        DEFAULT (GETUTCDATE()) FOR [ActualizadoEl];

    -- Índices
    -- 1) Lookup típico: verificar token => buscar por TokenHash activo y no usado
    CREATE INDEX [IX_EmailVerificationTokens_TokenHash_Activo]
        ON [dbo].[EmailVerificationTokens]([TokenHash], [Activo])
        INCLUDE ([UsuarioId], [ExpiraEl], [UsadoEl], [RevocadoEl]);

    -- 2) Gestión por usuario: invalidar tokens previos / buscar el último token
    CREATE INDEX [IX_EmailVerificationTokens_UsuarioId_Activo_CreadoEl]
        ON [dbo].[EmailVerificationTokens]([UsuarioId], [Activo], [CreadoEl] DESC)
        INCLUDE ([ExpiraEl], [UsadoEl], [RevocadoEl], [EmailEnviadoA]);
END

COMMIT TRANSACTION;
