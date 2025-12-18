use BioFXBD;

ALTER TABLE [dbo].[Producto]
ADD [PrecioFinal] AS
(
  CONVERT(decimal(18,2),
    ROUND([Precio] * (1 - (CONVERT(decimal(18,4), [Descuento]) / 100.0)), 2)
  )
) PERSISTED;
GO