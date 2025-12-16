use BioFXBD;

UPDATE Testimonios
SET Imagen = REPLACE(REPLACE(Imagen, '.png', '.webp'), '.jpg', '.webp')
WHERE Activo = 1
  AND (Imagen LIKE '%.png%' OR Imagen LIKE '%.jpg%');


select * from Testimonios