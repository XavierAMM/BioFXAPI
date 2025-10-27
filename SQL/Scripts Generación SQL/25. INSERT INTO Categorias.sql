USE BioFXBD;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;
BEGIN TRY
    update Categoria set Activo = 0 where Descripcion = 'Calmantes';
    
    DECLARE @NewCats TABLE(Descripcion NVARCHAR(500) NOT NULL);
    INSERT INTO @NewCats(Descripcion) VALUES
        (N'Mente y Concentración'),
        (N'Energía'),
        (N'Sistema Nervioso'),
        (N'Digestión'),
        (N'Sistema Inmune'),
        (N'Metabolismo'),
        (N'Relajación y Sueño'),
        (N'Colesterol'),
        (N'Salud Cardiovascular'),
        (N'Antioxidantes'),
        (N'Dolor'),
        (N'Huesos y Articulaciones'),
        (N'Embarazo y Lactancia'),
        (N'Muscular'),
        (N'Vitaminas y Minerales'),
        (N'Aceites Esenciales');

    ;WITH DistinctCats AS (
        SELECT DISTINCT LTRIM(RTRIM(Descripcion)) AS Descripcion
        FROM @NewCats
        WHERE Descripcion IS NOT NULL AND LTRIM(RTRIM(Descripcion)) <> N''
    )
    INSERT INTO dbo.Categoria (Descripcion, Activo, CreadoEl, ActualizadoEl)
    SELECT d.Descripcion, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
    FROM DistinctCats d
    LEFT JOIN dbo.Categoria c
      ON c.Descripcion = d.Descripcion
    WHERE c.Id IS NULL;

    COMMIT TRAN;
    PRINT 'Categorías insertadas si faltaban.';
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
