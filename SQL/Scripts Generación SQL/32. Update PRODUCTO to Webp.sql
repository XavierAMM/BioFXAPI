use BioFXBD;

UPDATE Producto
SET
    Imagen = REPLACE(REPLACE(Imagen, '.png', '.webp'), '.jpg', '.webp'),
    Logo   = REPLACE(REPLACE(Logo,   '.png', '.webp'), '.jpg', '.webp')
WHERE
    Imagen LIKE '%.png%' OR Imagen LIKE '%.jpg%'
    OR
    Logo   LIKE '%.png%' OR Logo   LIKE '%.jpg%';
