ALTER TABLE Usuario
ADD 
    nuevoEmailPendiente NVARCHAR(255) NULL,
    tokenCambioEmail NVARCHAR(6) NULL,
    expiracionTokenCambioEmail DATETIME2(7) NULL;