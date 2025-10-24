USE [BioFXBD];
SET NOCOUNT ON;
BEGIN TRY
    BEGIN TRAN;

    ------------------------------------------------------------
    -- 1) Desactivar todas las promociones actuales
    ------------------------------------------------------------
    UPDATE dbo.Promocion SET activa = 0;

    ------------------------------------------------------------
    -- 2) Insertar nuevas promociones desde JSON
    --    Mapea textoAlineacion / imagenAlineacion / textoPosicion a Alineacion.id
    --    Mapea fondo a Fondo.id por descripcion
    ------------------------------------------------------------
    DECLARE @json NVARCHAR(MAX) = N'
[
  {
    "id": 1,
    "titulo": "bioFX: Tu aliado en salud y bienestar",
    "descripcion": "Suplementos y vitaminas de alta calidad, respaldados por ciencia y medicina funcional.",
    "botonTexto": "Explora nuestros productos",
    "botonUrl": "categories",
    "imagen": "assets/extras/control.png",
    "background": "assets/extras/bg1.jpg",
    "textoAlineacion": "izquierda",
    "textoPosicion": "derecha",
    "imagenAlineacion": "izquierda",
    "fondo": "imagen",
    "colorTexto": "#ffffff"
  },
  {
    "id": 2,
    "titulo": "Descubre nuestras HERRAMIENTAS más populares",
    "descripcion": "Apoya tu sistema inmunológico, digestivo y energía diaria",
    "botonTexto": "Descubrir",
    "botonUrl": "#productos",
    "imagen": "assets/extras/banner2.png",
    "background": "assets/extras/bg2.jpg",
    "textoAlineacion": "izquierda",
    "textoPosicion": "derecha",
    "imagenAlineacion": "izquierda",
    "fondo": "imagen",
    "colorTexto": "#ffffff"
  },
  {
    "id": 3,
    "titulo": "Promoción exclusiva por tiempo limitado",
    "descripcion": "Envío gratis por lanzamiento de bioFX",
    "botonTexto": "Aprovechar oferta",
    "botonUrl": "#calmantes",
    "imagen": "assets/extras/control.png",
    "background": "assets/extras/bg3.jpg",
    "textoAlineacion": "izquierda",
    "textoPosicion": "derecha",
    "imagenAlineacion": "izquierda",
    "fondo": "imagen",
    "colorTexto": "#18474c"
  },
  {
    "id": 4,
    "titulo": "Respaldado por medicina funcional",
    "descripcion": "Cada producto seleccionado pensando en tu salud integral",
    "botonTexto": "Conócenos mejor",
    "botonUrl": "#calmantes",
    "imagen": "assets/extras/banner4.png",
    "background": "assets/extras/bg4.jpg",
    "textoAlineacion": "izquierda",
    "textoPosicion": "derecha",
    "imagenAlineacion": "izquierda",
    "fondo": "imagen",
    "colorTexto": "#ffffff"
  }
]';

    ;WITH J AS (
        SELECT *
        FROM OPENJSON(@json)
        WITH (
            id              INT            '$.id',
            titulo          NVARCHAR(200)  '$.titulo',
            descripcion     NVARCHAR(MAX)  '$.descripcion',
            botonTexto      NVARCHAR(100)  '$.botonTexto',
            botonUrl        NVARCHAR(300)  '$.botonUrl',
            imagen          NVARCHAR(500)  '$.imagen',
            background      NVARCHAR(300)  '$.background',
            textoAlineacion NVARCHAR(50)   '$.textoAlineacion',
            textoPosicion   NVARCHAR(50)   '$.textoPosicion',
            imagenAlineacion NVARCHAR(50)  '$.imagenAlineacion',
            fondo           NVARCHAR(50)   '$.fondo',
            colorTexto      NVARCHAR(20)   '$.colorTexto'
        )
    )
    INSERT INTO dbo.Promocion
    (
        titulo, descripcion, botonTexto, botonUrl, imagen,
        textoAlineacionId, imagenAlineacionId, fondoId,
        colorTexto, activa, fechaInicio, fechaFin, orden,
        creadoEl, actualizadoEl, background, textoPosicionId
    )
    SELECT
        j.titulo,
        j.descripcion,
        j.botonTexto,
        j.botonUrl,
        j.imagen,
        at.id          AS textoAlineacionId,
        ai.id          AS imagenAlineacionId,
        f.id           AS fondoId,
        j.colorTexto,
        1              AS activa,
        SYSUTCDATETIME(),   -- o GETDATE() si usas hora local del servidor
        NULL,
        ISNULL(j.id, 0) AS orden,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        j.background,
        ap.id          AS textoPosicionId
    FROM J j
    INNER JOIN dbo.Alineacion at ON at.descripcion = j.textoAlineacion
    INNER JOIN dbo.Alineacion ai ON ai.descripcion = j.imagenAlineacion
    INNER JOIN dbo.Alineacion ap ON ap.descripcion = j.textoPosicion
    INNER JOIN dbo.Fondo f       ON f.descripcion  = j.fondo
    WHERE NOT EXISTS (
        -- evita duplicados por título; ajusta si prefieres otra clave
        SELECT 1 FROM dbo.Promocion p WHERE p.titulo = j.titulo
    );

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

-- Verificación rápida
SELECT *
FROM dbo.Promocion
ORDER BY id DESC;
