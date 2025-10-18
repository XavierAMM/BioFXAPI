-- Tabla de alineaciones
CREATE TABLE Alineacion (
    id INT IDENTITY(1,1) PRIMARY KEY,
    descripcion NVARCHAR(50) NOT NULL
);

-- Insertar valores iniciales
INSERT INTO Alineacion (descripcion) VALUES 
('izquierda'),
('derecha'),
('arriba'),
('abajo'),
('centro');


-- Tabla de fondos
CREATE TABLE Fondo (
    id INT IDENTITY(1,1) PRIMARY KEY,
    descripcion NVARCHAR(50) NOT NULL
);

-- Insertar valor inicial
INSERT INTO Fondo (descripcion) VALUES 
('gradiente');


-- Tabla de promociones
CREATE TABLE Promocion (
    id INT IDENTITY(1,1) PRIMARY KEY,
    titulo NVARCHAR(200) NOT NULL,
    descripcion NVARCHAR(MAX) NULL,
    botonTexto NVARCHAR(100) NULL,
    botonUrl NVARCHAR(300) NULL,
    imagen NVARCHAR(500) NULL,
    textoAlineacionId INT NOT NULL FOREIGN KEY REFERENCES Alineacion(id),
    imagenAlineacionId INT NOT NULL FOREIGN KEY REFERENCES Alineacion(id),
    fondoId INT NOT NULL FOREIGN KEY REFERENCES Fondo(id),
    colorTexto NVARCHAR(20) NULL,
    activa BIT DEFAULT 1 NOT NULL,
    fechaInicio DATETIME2 DEFAULT GETDATE() NOT NULL,
    fechaFin DATETIME2 NULL,
    orden INT DEFAULT 0 NOT NULL,
    creadoEl DATETIME2 DEFAULT GETDATE() NOT NULL,
    actualizadoEl DATETIME2 DEFAULT GETDATE() NOT NULL
);
