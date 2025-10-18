CREATE TABLE Persona (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Nombre NVARCHAR(100) NOT NULL,
    Apellido NVARCHAR(100) NOT NULL,
    Telefono NVARCHAR(20) NULL,
    UsuarioId INT NOT NULL,
    Activo BIT NOT NULL DEFAULT 1,
    CreadoEl DATETIME2 DEFAULT GETDATE(),
    ActualizadoEl DATETIME2 DEFAULT GETDATE(),
    
    -- Clave foránea hacia Usuario
    CONSTRAINT FK_Persona_Usuario FOREIGN KEY (UsuarioId) 
        REFERENCES Usuario(Id) ON DELETE CASCADE,
    
    -- Restricción única para evitar múltiples personas por usuario
    CONSTRAINT UQ_Persona_UsuarioId UNIQUE (UsuarioId)
);