use BioFXBD;

ALTER TABLE [dbo].[Order]
ADD
  Subtotal   decimal(18,2) NULL,
  CostoEnvio   decimal(18,2) NULL,
  DescuentoUSD   decimal(18,2) NULL,
  tieneReceta  bit           NULL;
GO

ALTER TABLE [dbo].[Order] ADD CONSTRAINT DF_Order_Subtotal  DEFAULT (0) FOR Subtotal;
ALTER TABLE [dbo].[Order] ADD CONSTRAINT DF_Order_CostoEnvio  DEFAULT (5) FOR CostoEnvio;
ALTER TABLE [dbo].[Order] ADD CONSTRAINT DF_Order_DescuentoUSD  DEFAULT (0) FOR DescuentoUSD;
ALTER TABLE [dbo].[Order] ADD CONSTRAINT DF_Order_tieneReceta DEFAULT (0) FOR tieneReceta;
GO