CREATE TABLE Categoria (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Descripcion NVARCHAR(500) NULL,
    Activo BIT NOT NULL DEFAULT 1,
    CreadoEl DATETIME NOT NULL DEFAULT GETUTCDATE(),
    ActualizadoEl DATETIME NOT NULL DEFAULT GETUTCDATE()
);

INSERT INTO Categoria (Descripcion) VALUES 
('Concentración'),('Calmantes');

-- Eliminar columna Categoria existente
ALTER TABLE Producto DROP COLUMN Categoria;

-- Agregar columna CategoriaId
ALTER TABLE Producto ADD CategoriaId INT NULL;

-- Agregar foreign key constraint
ALTER TABLE Producto 
ADD CONSTRAINT FK_Producto_Categoria FOREIGN KEY (CategoriaId) 
REFERENCES Categoria(Id);

