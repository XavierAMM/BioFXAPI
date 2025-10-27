USE [BioFXBD];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

BEGIN TRY
    -- 1) Desactivar testimonios vigentes
    UPDATE dbo.Testimonios
    SET Activo = 0,
        ActualizadoEl = SYSUTCDATETIME()
    WHERE Activo = 1;

    -- 2) Insertar nuevos (desde tu JSON actual)
    INSERT INTO dbo.Testimonios
        (Nombre, Testimonio, Imagen, Valoracion, Activo, CreadoEl, ActualizadoEl)
    VALUES
    (N'Jorge G',
     N'La atención fue excelente desde el primer contacto: me explicaron claramente los beneficios antiinflamatorios de Curcetin (la combinación de cúrcuma, quercetina, magnesio y vitamina C es fantástica). El envío fue rapidísimo, lo recibí en menos de 24 horas. Y venía con una sorpresa. Fue un detalle inesperado que me encantó.',
     N'assets/extras/jorgeg.jpg', 5, 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
    (N'Jennifer G',
     N'Pedí Glutaceon porque buscaba un buen antioxidante para mi. El equipo fue muy amable y profesional, resolvió todas mis dudas. El paquete llegó al día siguiente y contenía una tarjeta de agradecimiento. ¡Pequeños detalles que generan mucha confianza!',
     N'assets/extras/jenniferg.jpg', 5, 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
    (N'Fernanda A',
     N'La atención desde el call center fue impecable: me recomendaron Vegezym por mis problemas digestivos y me explicaron cómo sus enzimas me ayudarían. En menos dos días ya tenía el producto en casa (vivo en Provincia), y dentro hubo una bolsita con barritas saludables como obsequio. Un gesto muy agradable.',
     N'assets/extras/fernandaa.jpg', 4, 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
    (N'Juan Pablo M',
     N'Compré DKMulsion porque necesitaba un suplemento de vitamina D y K para fortalecer mis huesos. Desde el call center me atendieron con muchísima paciencia, explicándome cómo tomarlo y resolviendo todas mis dudas. El pedido llegó al siguiente día, perfectamente empacado. Me sentí muy bien atendido y definitivamente volveré a comprar.',
     N'assets/extras/juanpablom.jpg', 5, 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
    (N'Lizeth T',
     N'Estaba buscando un suplemento para mejorar mis niveles de hierro y me recomendaron Hemogest. La atención fue excelente, me explicaron sus beneficios para el sistema circulatorio y la vitalidad. Hice el pedido por BioFX y el proceso fue muy sencillo. En menos de 48 horas ya lo tenía en mis manos.',
     N'assets/extras/lizetht.jpg', 5, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

    COMMIT TRAN;
    PRINT 'Testimonios actualizados correctamente.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRAN;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrNum INT = ERROR_NUMBER();
    DECLARE @ErrState INT = ERROR_STATE();

    RAISERROR(N'Error %d (state %d): %s', 16, 1, @ErrNum, @ErrState, @ErrMsg);
END CATCH;
GO
