-- Tabla Producto
CREATE TABLE Producto (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Codigo NVARCHAR(50) NOT NULL UNIQUE,
    Disponible BIT NOT NULL DEFAULT 1,
    Nombre NVARCHAR(100) NOT NULL,
    Precio DECIMAL(18,2) NOT NULL,
    Imagen NVARCHAR(255) NOT NULL,
    Logo NVARCHAR(255) NOT NULL,
    Descripcion NVARCHAR(MAX) NOT NULL,
    Categoria NVARCHAR(50) NOT NULL,
    Desc_Principal NVARCHAR(MAX) NOT NULL,
    Desc_Otros NVARCHAR(MAX) NULL,
    Descuento INT NOT NULL DEFAULT 0,
    Disclaimer NVARCHAR(MAX) NULL,
    Activo BIT NOT NULL DEFAULT 1,
    CreadoEl DATETIME NOT NULL DEFAULT GETUTCDATE(),
    ActualizadoEl DATETIME NOT NULL DEFAULT GETUTCDATE()
);

-- Tabla intermedia para productos promocionados
CREATE TABLE ProductoPromocionado (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ProductoId INT NOT NULL,
    PromocionadoId INT NOT NULL,
    Activo BIT NOT NULL DEFAULT 1,
    CreadoEl DATETIME NOT NULL DEFAULT GETUTCDATE(),
    ActualizadoEl DATETIME NOT NULL DEFAULT GETUTCDATE(),
    FOREIGN KEY (ProductoId) REFERENCES Producto(Id),
    FOREIGN KEY (PromocionadoId) REFERENCES Producto(Id)
);