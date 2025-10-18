USE [BioFXBD]
GO

/****** Object:  Table [dbo].[Usuario]    Script Date: 9/4/2025 7:46:11 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[Usuario](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[email] [nvarchar](255) NOT NULL,
	[contrasenaHash] [nvarchar](255) NOT NULL,
	[creadoEl] [datetime2](7) NULL,
	[intentosFallidos] [int] NOT NULL,
	[bloqueadoHasta] [datetime2](7) NULL,
	[ultimoLogin] [datetime2](7) NULL,
	[fechaActualizacionContrasena] [datetime2](7) NULL,
	[requiereReinicioContrasena] [bit] NOT NULL,
	[emailConfirmado] [bit] NOT NULL,
	[tokenVerificacionEmail] [nvarchar](100) NULL,
	[expiracionTokenVerificacion] [datetime2](7) NULL,
	[tokenResetContrasena] [nvarchar](100) NULL,
	[expiracionTokenResetContrasena] [datetime2](7) NULL,
	[actualizadoEl] [datetime2](7) NULL,
	[ultimaIpLogin] [nvarchar](45) NULL,
	[esAdministrador] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
UNIQUE NONCLUSTERED 
(
	[email] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

ALTER TABLE [dbo].[Usuario] ADD  DEFAULT (getdate()) FOR [creadoEl]
GO

ALTER TABLE [dbo].[Usuario] ADD  DEFAULT ((0)) FOR [intentosFallidos]
GO

ALTER TABLE [dbo].[Usuario] ADD  DEFAULT ((0)) FOR [requiereReinicioContrasena]
GO

ALTER TABLE [dbo].[Usuario] ADD  DEFAULT ((0)) FOR [emailConfirmado]
GO

ALTER TABLE [dbo].[Usuario] ADD  DEFAULT ((0)) FOR [esAdministrador]
GO


