UPDATE dbo.Usuario
SET creadoEl = CAST(creadoEl AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE creadoEl IS NOT NULL;

UPDATE dbo.Usuario
SET bloqueadoHasta = CAST(bloqueadoHasta AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE bloqueadoHasta IS NOT NULL;

UPDATE dbo.Usuario
SET ultimoLogin = CAST(ultimoLogin AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE ultimoLogin IS NOT NULL;

UPDATE dbo.Usuario
SET fechaActualizacionContrasena = CAST(fechaActualizacionContrasena AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE fechaActualizacionContrasena IS NOT NULL;

UPDATE dbo.Usuario
SET expiracionTokenVerificacion = CAST(expiracionTokenVerificacion AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE expiracionTokenVerificacion IS NOT NULL;

UPDATE dbo.Usuario
SET expiracionTokenResetContrasena = CAST(expiracionTokenResetContrasena AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE expiracionTokenResetContrasena IS NOT NULL;

UPDATE dbo.Usuario
SET actualizadoEl = CAST(actualizadoEl AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE actualizadoEl IS NOT NULL;

UPDATE dbo.Usuario
SET expiracionTokenCambioEmail = CAST(expiracionTokenCambioEmail AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE expiracionTokenCambioEmail IS NOT NULL;

UPDATE dbo.Promocion
SET fechaInicio = CAST(fechaInicio AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE fechaInicio IS NOT NULL;

UPDATE dbo.Promocion
SET fechaFin = CAST(fechaFin AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE fechaFin IS NOT NULL;

UPDATE dbo.Promocion
SET creadoEl = CAST(creadoEl AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE creadoEl IS NOT NULL;

UPDATE dbo.Promocion
SET actualizadoEl = CAST(actualizadoEl AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE actualizadoEl IS NOT NULL;

UPDATE dbo.Persona
SET CreadoEl = CAST(CreadoEl AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE CreadoEl IS NOT NULL;

UPDATE dbo.Persona
SET ActualizadoEl = CAST(ActualizadoEl AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE ActualizadoEl IS NOT NULL;

UPDATE dbo.Fondo
SET CreadoEl = CAST(CreadoEl AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE CreadoEl IS NOT NULL;

UPDATE dbo.Fondo
SET ActualizadoEl = CAST(ActualizadoEl AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE ActualizadoEl IS NOT NULL;

UPDATE dbo.Alineacion
SET CreadoEl = CAST(CreadoEl AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE CreadoEl IS NOT NULL;

UPDATE dbo.Alineacion
SET ActualizadoEl = CAST(ActualizadoEl AT TIME ZONE 'SA Pacific Standard Time' AT TIME ZONE 'UTC' AS datetime2)
WHERE ActualizadoEl IS NOT NULL;

