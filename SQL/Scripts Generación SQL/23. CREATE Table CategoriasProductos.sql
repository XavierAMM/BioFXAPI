USE [BioFXBD];
GO
SET NOCOUNT ON;

IF OBJECT_ID('dbo.CategoriasProductos','U') IS NULL
BEGIN
    CREATE TABLE dbo.CategoriasProductos
    (
        Id             INT IDENTITY(1,1) NOT NULL,
        ProductoId     INT NOT NULL,
        CategoriaId    INT NOT NULL,
        Activo         BIT NOT NULL CONSTRAINT DF_CatProd_Activo DEFAULT (1),
        CreadoEl       DATETIME NOT NULL CONSTRAINT DF_CatProd_CreadoEl DEFAULT (GETUTCDATE()),
        ActualizadoEl  DATETIME NOT NULL CONSTRAINT DF_CatProd_ActualizadoEl DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_CategoriasProductos PRIMARY KEY CLUSTERED (Id ASC)
    );

    -- Evita duplicados producto–categoría
    ALTER TABLE dbo.CategoriasProductos
      ADD CONSTRAINT UQ_CategoriasProductos_Prod_Cat UNIQUE (ProductoId, CategoriaId);

    -- FK a Producto y Categoria
    ALTER TABLE dbo.CategoriasProductos
      ADD CONSTRAINT FK_CategoriasProductos_Producto
          FOREIGN KEY (ProductoId) REFERENCES dbo.Producto(Id);

    ALTER TABLE dbo.CategoriasProductos
      ADD CONSTRAINT FK_CategoriasProductos_Categoria
          FOREIGN KEY (CategoriaId) REFERENCES dbo.Categoria(Id);

    -- Índices de ayuda
    CREATE NONCLUSTERED INDEX IX_CategoriasProductos_ProductoId ON dbo.CategoriasProductos(ProductoId);
    CREATE NONCLUSTERED INDEX IX_CategoriasProductos_CategoriaId ON dbo.CategoriasProductos(CategoriaId);
END
ELSE
BEGIN
    PRINT 'dbo.CategoriasProductos ya existe.';
END
GO
