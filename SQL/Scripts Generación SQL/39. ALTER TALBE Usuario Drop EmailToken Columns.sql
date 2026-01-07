Use BioFXBD;

BEGIN TRANSACTION;

-- Seguridad: solo dropear si existen
IF COL_LENGTH('dbo.Usuario', 'tokenVerificacionEmail') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Usuario DROP COLUMN tokenVerificacionEmail;
END

IF COL_LENGTH('dbo.Usuario', 'expiracionTokenVerificacion') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Usuario DROP COLUMN expiracionTokenVerificacion;
END

COMMIT TRANSACTION;
