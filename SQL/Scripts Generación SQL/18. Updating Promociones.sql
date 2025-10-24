USE [BioFXBD];
GO

-- background: ruta de imagen
IF COL_LENGTH('dbo.Promocion', 'background') IS NULL
    ALTER TABLE dbo.Promocion ADD [background] NVARCHAR(300) NULL;
GO

-- textoPosicionId: FK a Alineacion(id)
IF COL_LENGTH('dbo.Promocion', 'textoPosicionId') IS NULL
    ALTER TABLE dbo.Promocion ADD [textoPosicionId] INT NULL;
GO

-- FK (LEFT NULL hasta que carguemos datos)
IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_Promocion_textoPosicionId_Alineacion'
)
    ALTER TABLE dbo.Promocion
    WITH CHECK ADD CONSTRAINT FK_Promocion_textoPosicionId_Alineacion
    FOREIGN KEY (textoPosicionId) REFERENCES dbo.Alineacion(id);
GO

-- Índice recomendado para consultas por posición
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Promocion_textoPosicionId' AND object_id = OBJECT_ID('dbo.Promocion'))
    CREATE INDEX IX_Promocion_textoPosicionId ON dbo.Promocion(textoPosicionId);
GO
