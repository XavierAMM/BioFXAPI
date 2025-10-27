USE [BioFXBD];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;
BEGIN TRY
    /* 0) Fuente deseada -> #Src */
    IF OBJECT_ID('tempdb..#Src') IS NOT NULL DROP TABLE #Src;
    CREATE TABLE #Src(
        ProductoId INT NOT NULL,
        CategoriaId INT NOT NULL,
        CONSTRAINT PK_#Src PRIMARY KEY (ProductoId, CategoriaId)
    );

    INSERT INTO #Src(ProductoId, CategoriaId)
    VALUES
        -- 1 ADAPTESSENS
        (1,11),(1,9),(1,17),
        -- 2 CALMESSENS
        (2,11),(2,14),(2,17),
        -- 3 COLESSENS
        (3,5),(3,12),
        -- 4 CORAESSENS
        (4,15),
        -- 5 CURCETIN
        (5,4),(5,7),
        -- 8 BUTIREX
        (8,6),(8,16),(8,12),
        -- 9 DK-MULSION
        (9,10),(9,8),
        -- 10 ENTEROPLEX
        (10,6),
        -- 11 GASTROESSENS
        (11,6),
        -- 12 GLUTACEON
        (12,4),(12,9),
        -- 13 HARPAGOSSENS
        (13,10),(13,7),
        -- 14 HEMOGEST
        (14,8),
        -- 15 INMUNOPLEX
        (15,16),
        -- 16 KETOESSENS
        (16,12),(16,9),
        -- 17 LAXESSENS
        (17,6),
        -- 18 MITOESSENS
        (18,9),(18,13),
        -- 19 MULTIESSENS MINERALES
        (19,18),
        -- 20 MULTIESSENS VITAMINAS
        (20,18),
        -- 21 MYOESSENS
        (21,13),(21,6),(21,9),(21,7),
        -- 22 NEURESSENS
        (22,17),(22,13),(22,7),
        -- 23 OMEGAESSENS
        (23,15),(23,5),(23,8),
        -- 24 PANCREOGEN
        (24,12),
        -- 25 P-CIDE
        (25,3),
        -- 26 PROBIOESSENS
        (26,6),
        -- 27 QUINTESSENS
        (27,4),(27,9),
        -- 28 S-CIDE
        (28,3),
        -- 29 V-CIDE
        (29,3),
        -- 30 VEGEZYM
        (30,6);

    /* 1) Upsert: activar si existe, insertar si falta */
    MERGE dbo.CategoriasProductos AS T
    USING #Src AS S
      ON T.ProductoId = S.ProductoId AND T.CategoriaId = S.CategoriaId
    WHEN MATCHED THEN
        UPDATE SET T.Activo = 1,
                   T.ActualizadoEl = SYSUTCDATETIME()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (ProductoId, CategoriaId, Activo, CreadoEl, ActualizadoEl)
        VALUES (S.ProductoId, S.CategoriaId, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

    /* 2) Desactivar relaciones sobrantes de estos productos */
    UPDATE CP
       SET CP.Activo = 0,
           CP.ActualizadoEl = SYSUTCDATETIME()
    FROM dbo.CategoriasProductos CP
    WHERE EXISTS (SELECT 1 FROM #Src x WHERE x.ProductoId = CP.ProductoId)
      AND NOT EXISTS (SELECT 1 FROM #Src x
                      WHERE x.ProductoId = CP.ProductoId
                        AND x.CategoriaId = CP.CategoriaId);

    COMMIT TRAN;
    PRINT 'CategoriasProductos sincronizada. Inserts/updates OK, sobrantes desactivados.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRAN;
    DECLARE @ErrMsg NVARCHAR(4000)=ERROR_MESSAGE(),
            @ErrNum INT=ERROR_NUMBER(),
            @ErrState INT=ERROR_STATE(),
            @ErrLine INT=ERROR_LINE();
    RAISERROR(N'Error %d (state %d, line %d): %s',16,1,@ErrNum,@ErrState,@ErrLine,@ErrMsg);
END CATCH;
GO
