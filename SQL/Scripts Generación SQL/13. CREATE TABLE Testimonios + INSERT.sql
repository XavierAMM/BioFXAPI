use BioFXBD;
CREATE TABLE [dbo].[Testimonios](
    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Nombre] [nvarchar](255) NOT NULL,
    [Texto] [nvarchar](max) NOT NULL,
    [Imagen] [nvarchar](500) NULL,
    [Valoracion] [int] NOT NULL,
    [Activo] [bit] NOT NULL DEFAULT 1,
    [CreadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [ActualizadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [CK_Testimonios_Valoracion] CHECK ([Valoracion] BETWEEN 1 AND 5)
);
GO

INSERT INTO [dbo].[Testimonios] (
    [Nombre],
    [Texto],
    [Imagen],
    [Valoracion]
) VALUES
('Ana García', 'Los productos de Bio-fx cambiaron mi vida. Después de años de problemas de concentración, finalmente encontré una solución natural que realmente funciona.', 'assets/extras/pou1.jpg', 5),
('Carlos Rodríguez', 'Increíble la calidad de los productos. Como médico, recomiendo Bio-fx a mis pacientes por su enfoque en la medicina funcional y resultados comprobables.', 'assets/extras/pou2.jpg', 5),
('María López', 'El servicio al cliente es excepcional y los productos superaron mis expectativas. He notado mejoras significativas en mi bienestar general en solo un mes.', 'assets/extras/pou1.jpg', 4),
('Javier Martínez', 'Como atleta, necesito suplementos de calidad. Bio-fx ofrece exactamente lo que buscaba: productos efectivos, naturales y con respaldo científico.', 'assets/extras/pou2.jpg', 5),
('Laura Fernández', 'Después de probar muchas alternativas, Bio-fx es la única que me ha dado resultados reales y duraderos. ¡No puedo estar más satisfecha!', 'assets/extras/pou1.jpg', 5);

GO