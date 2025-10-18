-- Actualizar productos existentes con categorías
UPDATE Producto SET CategoriaId = 1 WHERE Id IN (1, 3, 5, 6); -- Concentración
UPDATE Producto SET CategoriaId = 2 WHERE Id IN (2, 4, 7); -- Calmantes