USE [BioFXBD];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;
BEGIN TRY
    -- 1) Quitar FK antiguo si existe
    IF EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = 'FK_Producto_Categoria'
          AND parent_object_id = OBJECT_ID('dbo.Producto')
    )
    BEGIN
        ALTER TABLE dbo.Producto DROP CONSTRAINT FK_Producto_Categoria;
    END

    -- 2) Quitar columna CategoriaId si existe
    IF COL_LENGTH('dbo.Producto','CategoriaId') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.Producto DROP COLUMN CategoriaId;
    END

    -- 3) Agregar columna Contraindicaciones si no existe
    IF COL_LENGTH('dbo.Producto','Contraindicaciones') IS NULL
    BEGIN
        ALTER TABLE dbo.Producto
            ADD Contraindicaciones NVARCHAR(MAX) NULL;
        -- (opcional) DEFAULT a futuro:
        -- ALTER TABLE dbo.Producto
        --   ADD CONSTRAINT DF_Producto_Contraindicaciones DEFAULT (NULL) FOR Contraindicaciones;
    END

    COMMIT TRAN;
    PRINT 'Producto: eliminado FK_Producto_Categoria y columna CategoriaId; agregada columna Contraindicaciones.';
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
