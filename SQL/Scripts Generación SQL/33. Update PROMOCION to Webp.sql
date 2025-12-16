USE BioFXBD;
GO

UPDATE Promocion
SET
    imagen     = REPLACE(REPLACE(imagen,     '.png', '.webp'), '.jpg', '.webp'),
    background = REPLACE(REPLACE(background, '.png', '.webp'), '.jpg', '.webp')
WHERE
    imagen     LIKE '%.png%' OR imagen     LIKE '%.jpg%'
    OR
    background LIKE '%.png%' OR background LIKE '%.jpg%';

SELECT *
FROM Promocion;
