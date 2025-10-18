UPDATE [dbo].[Producto] SET 
    [Stock] = CASE WHEN [Disponible] = 1 THEN 100 ELSE 0 END,
    [StockReservado] = 0;