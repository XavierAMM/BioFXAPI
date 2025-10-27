USE [BioFXBD];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;
BEGIN TRY
    /* 0) Desactivar relleno */
    UPDATE dbo.Producto
    SET Activo = 0, Disponible = 0, ActualizadoEl = SYSUTCDATETIME()
    WHERE UPPER(Nombre) IN (N'LECHUGUIN', N'EJEMPLO');

    /* 1) JSON fuente */
    DECLARE @json NVARCHAR(MAX) = N'[
    {
        "id": 1,
        "codigo": "8039-ALE-0818",
        "disponible": true,
        "nombre": "ADAPTESSENS",
        "precio": 36,
        "imagen": "assets/productos/adaptessens.png",
        "logo": "assets/logos/adaptessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Maral, Ashwagandha, Schisandra, Eleuthero, Biotina y minerales esenciales como Zinc y Manganeso.",
        "categoria": ["Mente y Concentración", "Energía", "Sistema Nervioso"],
        "descripciones": {
            "principal": "Aporta nutrientes que contribuyen al sistema inmunitario, la función cognitiva, el metabolismo energético y a la regulación de estrés.",
            "otros": "**Dósis Sugerida:** 2-3 Cápsulas diarias.\n**Contenido:** 90 cápsulas.\n**Tiempo del tratamiento:** Máximo 1 mes*."
        },
        "contraindicaciones": "No recomendado para mujeres embarazadas o en período de lactancia. Personas con condiciones médicas preexistentes deben consultar con su médico antes de usar.",
        "promocionados": [3, 20, 25],
        "descuento": 0,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 2,
        "codigo": "24158-ALE-0524",
        "disponible": true,
        "nombre": "BUTIREX",
        "precio": 27,
        "imagen": "assets/productos/butirex.png",
        "logo": "assets/logos/butirex.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Butirato de Magnesio, una sal altamente estable, y Acetato de Retinilo (Vitamina A).",
        "categoria": ["Digestión", "Sistema Inmune", "Metabolismo"],
        "descripciones": {
            "principal": "Apoya la salud digestiva y puede ser útil como soporte en el cuidado del colon. Coadyuvante al metabolismo energético, al funcionamiento psicológico normal y al mantenimiento del sistema inmunitario sobre funciones metabólicas.",
            "otros": "**Dósis Sugerida:** 2 Cápsulas.\n**Contenido:** 60 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado en casos de hipersensibilidad a alguno de los componentes. Consultar con profesional de la salud durante embarazo y lactancia.",
        "promocionados": [8, 9, 15],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 3,
        "codigo": "8037-ALE-0718",
        "disponible": true,
        "nombre": "CALMESSENS",
        "precio": 34,
        "imagen": "assets/productos/calmessens.png",
        "logo": "assets/logos/calmessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Colina, Vitamina B1, Niacina, Ácido Pantoténico y Manganeso.",
        "categoria": ["Mente y Concentración", "Relajación y Sueño", "Sistema Nervioso"],
        "descripciones": {
            "principal": "Favorece la memoria, la concentración y la relajación, contribuyendo a una mejor calidad del sueño. Apoya en situaciones de estrés, fatiga y agitación, además de ser un aliado frente a problemas de atención y memoria.",
            "otros": "**Dósis Sugerida:** 3 Cápsulas.\n**Contenido:** 60 cápsulas.\n**Tiempo del tratamiento:** 20 Días."
        },
        "contraindicaciones": "No se recomienda en casos de hipersensibilidad a los componentes. Consultar con médico durante embarazo y lactancia.",
        "promocionados": [1, 20, 19],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 4,
        "codigo": "12159-ALE-0120",
        "disponible": true,
        "nombre": "COLESSENS",
        "precio": 43,
        "imagen": "assets/productos/colessens.png",
        "logo": "assets/logos/colessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Bergamota, Ajo, Corteza de Pino, Saccharum Officinarum, Magnesio y Vitaminas Esenciales.",
         "categoria": ["Colesterol", "Metabolismo"],
        "descripciones": {
            "principal": "Ayuda a reducir y modular los niveles de colesterol y triglicéridos, siendo una alternativa de soporte en casos de intolerancia a estatinas, hiperlipidemia, hipercolesterolemia y dislipidemias mixtas.",
            "otros": "**Dósis Sugerida:** 2 Cápsulas antes de dormir.\n**Contenido:** 60 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado durante embarazo y lactancia. Personas con enfermedades hepáticas deben consultar con su médico antes de usar.",
        "promocionados": [5, 21, 25],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 5,
        "codigo": "10537-ALE-0619",
        "disponible": true,
        "nombre": "CORAESSENS",
        "precio": 36,
        "imagen": "assets/productos/coraessens.png",
        "logo": "assets/logos/coraessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Corazón Bovino Liofilizado, Magnesio, B12 (Metilcobalamina), B6 (Piridoxal-5-fosfato) y B9 (L-5 Metiltetrahidrofolato) en sus formas más activas.",
        "categoria": ["Salud Cardiovascular"],
        "descripciones": {
            "principal": "Apoya la salud cardiovascular, siendo útil después de procesos de detoxificación. Favorece la función hepática y puede contribuir en casos relacionados con desequilibrios.",
            "otros": "**Dósis Sugerida:** 3 Cápsulas.\n**Contenido:** 90 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado para personas con alergia a productos bovinos. Consultar con médico en casos de condiciones cardíacas preexistentes.",
        "promocionados": [21, 25, 4],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 6,
        "codigo": "8066-ALE-0818",
        "disponible": true,
        "nombre": "CURCETIN",
        "precio": 47.5,
        "imagen": "assets/productos/curcetin.png",
        "logo": "assets/logos/curcetin.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con MAGNESIO, VITAMINA C, CÚRCUMA LONGA Y QUERCETINA.",
        "categoria": ["Antioxidantes", "Dolor"],
        "descripciones": {
            "principal": "Su combinación natural aporta acción antioxidante y contribuye a modular procesos inflamatorios crónicos, brindando soporte nutricional complementario al organismo.",
            "otros": "**Dósis Sugerida:** 3 Cápsulas.\n**Contenido:** 90 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado en casos de obstrucción biliar o cálculos biliares. Personas con trastornos de coagulación deben consultar con su médico.",
        "promocionados": [10, 13, 18],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 7,
        "codigo": "8041-ALE-0818",
        "disponible": true,
        "nombre": "DK-MULSION",
        "precio": 30,
        "imagen": "assets/productos/dk-mulsion.png",
        "logo": "assets/logos/dk-mulsion.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula líquida con Vitamina D3 y Vitamina K2.",
        "categoria": ["Huesos y Articulaciones", "Embarazo y Lactancia"],
        "descripciones": {
            "principal": "Contribuye a la adecuada fijación del calcio en los huesos y ayuda a prevenir problemas de osteoporosis. Además, favorece el fortalecimiento del sistema inmune y apoya la salud articular y cardiovascular.",
            "otros": "**Dósis Sugerida:** 4 Gotas al día.\n**Contenido:** 30 ml.\n**Tiempo del tratamiento:** 60 Días."
        },
        "contraindicaciones": "No recomendado en casos de hipercalcemia o hipervitaminosis D. Consultar con médico si se está tomando anticoagulantes.",
        "promocionados": [11, 19, 21],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 8,
        "codigo": "7825-ALE-0718",
        "disponible": true,
        "nombre": "ENTEROPLEX",
        "precio": 54.5,
        "imagen": "assets/productos/enteroplex.png",
        "logo": "assets/logos/enteroplex.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con L-Glutamina, Aloe Vera y Vitamina A.",
        "categoria": ["Digestión"],
        "descripciones": {
            "principal": "Actúa como regenerador y antiinflamatorio natural, favoreciendo la reparación y cicatrización de la mucosa gástrica e intestinal. Indicado como soporte nutricional en casos de gastritis, colon irritable y colitis.",
            "otros": "**Dósis Sugerida:** 2 Scoops de 3 gr cada una.\n**Contenido:** 180 gr.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "Prohibido en embarazo (consultar con profesional de la salud). No recomendado en casos de enfermedad renal severa.",
        "promocionados": [2, 9, 24],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 9,
        "codigo": "8067-ALE-0818",
        "disponible": true,
        "nombre": "GASTROESSENS",
        "precio": 31,
        "imagen": "assets/productos/gastroessens.png",
        "logo": "assets/logos/gastroessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Clorhidrato de Betaína, Pepsina y Zinc.",
        "categoria": ["Digestión"],
        "descripciones": {
            "principal": "Contribuye a minimizar síntomas de gastritis, como hinchazón tras las comidas y reflujo, favoreciendo una digestión más eficiente, especialmente después del uso prolongado de antiácidos.",
            "otros": "**Dósis Sugerida:** 3 Cápsulas.\n**Contenido:** 90 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado en pacientes con gastritis. Consultar con médico en casos de úlcera péptica activa.",
        "promocionados": [2, 8, 28],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 10,
        "codigo": "11640-ALE-1119",
        "disponible": true,
        "nombre": "GLUTACEON",
        "precio": 56.5,
        "imagen": "assets/productos/glutaceon.png",
        "logo": "assets/logos/glutaceon.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Glutatión, Vitamina B2, Vitamina B3 y Selenio.",
        "categoria":["Antioxidantes", "Energía"],
        "descripciones": {
            "principal": "Potente antioxidante que ayuda a eliminar toxinas, medicamentos y metales pesados; desacelera el proceso de envejecimiento celular; desintoxica el hígado y las células; y refuerza la función inmune.",
            "otros": "**Dósis Sugerida:** 2 Cápsulas.\n**Contenido:** 60 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado en casos de asma o alergias. Consultar con médico durante embarazo y lactancia.",
        "promocionados": [6, 13, 18],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 11,
        "codigo": "362-PNE-0822",
        "disponible": true,
        "nombre": "HARPAGOSSENS",
        "precio": 45,
        "imagen": "assets/productos/harpagossens.png",
        "logo": "assets/logos/harpagossens.png",
        "descripcion": "**Fitoterapéutico - NUTRABIOTICS®** \nFórmula con extracto estandarizado de Harpagophytum Procumbens.",
        "categoria": ["Huesos y Articulaciones", "Dolor"],
        "descripciones": {
            "principal": "Coadyuvante en el alivio sintomático de artritis, osteoartritis reumatoide, tendinitis y lumbalgias; protege el tejido articular; favorece la recuperación de la movilidad; sin los efectos adversos de antiinflamatorios convencionales.",
            "otros": "**Dósis Sugerida:** 2 Tabletas.\n**Contenido:** 30 tabletas.\n**Tiempo del tratamiento:** 15 Días."
        },
        "contraindicaciones": "No recomendado en casos de úlcera gástrica o duodenal. Evitar durante embarazo y lactancia.",
        "promocionados": [7, 19, 21],
        "descuento": 10,
        "disclaimer": "Este producto esta dirigido a tratar el efecto sintomático. No es curativo. No exceder su consumo. Leer indicaciones y contraindicaciones. **Consulte con su profesional de salud.**"
    },
    {
        "id": 12,
        "codigo": "17850-ALE-1221",
        "disponible": true,
        "nombre": "HEMOGEST",
        "precio": 33,
        "imagen": "assets/productos/hemogest.png",
        "logo": "assets/logos/hemogest.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con 5-Metiltetrahidrofolato sal de glucosamina (Folato) y Hierro Bisglicinato Quelado (Hierro Quelado).",
        "categoria": ["Embarazo y Lactancia"],
        "descripciones": {
            "principal": "Aporta hierro biodisponible y folato activo para prevenir la anemia en el periodo prenatal y favorecer el desarrollo óptimo del feto; refuerza la formación de glóbulos rojos y la producción de energía celular.",
            "otros": "**Dósis Sugerida:** 2 Cápsulas lejos de las comidas.\n**Contenido:** 60 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado en casos de hemocromatosis o sobrecarga de hierro. Consultar con médico durante embarazo.",
        "promocionados": [13, 18, 21],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 13,
        "codigo": "8559-ALE-1018",
        "disponible": true,
        "nombre": "INMUNOPLEX",
        "precio": 44,
        "imagen": "assets/productos/inmunoplex.png",
        "logo": "assets/logos/inmunoplex.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Calostro y Bazo Bovino Liofilizado, Glicoproteínas, Vitamina D3, Zinc y Prebióticos.",
        "categoria": ["Sistema Inmune"],
        "descripciones": {
            "principal": "Refuerza el sistema inmunológico; ayuda a prevenir infecciones recurrentes en adultos y niños; contribuye al control de infecciones virales y reacciones alérgicas.",
            "otros": "**Dósis Sugerida:** 3 Cápsulas.\n**Contenido:** 90 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado para personas con alergia a productos lácteos o bovinos. Consultar con médico en casos de enfermedades autoinmunes.",
        "promocionados": [10, 12, 18],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 14,
        "codigo": "15091-ALE-0221",
        "disponible": true,
        "nombre": "KETOESSENS",
        "precio": 55.5,
        "imagen": "assets/productos/ketoessens.png",
        "logo": "assets/logos/ketoessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con MCT de coco (Ácidos Grasos de Cadena Media) y Fibra de Acacia.",
        "categoria": ["Metabolismo", "Energía"],
        "descripciones": {
            "principal": "Aporta energía rápida; favorece dietas cetogénicas, bajas en carbohidratos y períodos de ayuno; coadyuvante de pérdida de peso; ayuda a regular glucosa, insulina y sensibilidad insulínica; actúa como prebiótico.",
            "otros": "**Dósis Sugerida:** 1 Scoop (10 gr).\n**Contenido:** 300 gr.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado en casos de enfermedad hepática o pancreática. Consultar con médico en diabetes.",
        "promocionados": [16, 22, 28],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No superar la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 15,
        "codigo": "377-PNE-1222",
        "disponible": true,
        "nombre": "LAXESSENS",
        "precio": 17.5,
        "imagen": "assets/productos/laxessens.png",
        "logo": "assets/logos/laxessens.png",
        "descripcion": "**Fitoterapéutico - NUTRABIOTICS®** \nFórmula con extractos fitoterapéuticos de uso tradicional con acción laxante natural.",
        "categoria": ["Digestión"],
        "descripciones": {
            "principal": "Coadyuvante en el tratamiento del estreñimiento crónico.",
            "otros": "**Dósis Sugerida:** 2-4 Cápsulas al día.\n**Contenido:** 30 cápsulas.\n**Tiempo del tratamiento:** 5 Días*."
        },
        "contraindicaciones": "No usar en casos de obstrucción intestinal, enfermedad de Crohn o colitis ulcerosa. No recomendado durante embarazo.",
        "promocionados": [2, 8, 24],
        "descuento": 10,
        "disclaimer": "Este producto esta dirigido a tratar el efecto sintomático. No es curativo. No exceder su consumo. Leer indicaciones y contraindicaciones. **Consulte con su profesional de salud.**"
    },
    {
        "id": 16,
        "codigo": "18514-ALE-0322",
        "disponible": true,
        "nombre": "MITOESSENS",
        "precio": 56.5,
        "imagen": "assets/productos/mitoessens.png",
        "logo": "assets/logos/mitoessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con D-Ribosa y Nicotinamida.",
        "categoria": ["Energía", "Muscular"],
        "descripciones": {
            "principal": "Proporciona energía a nivel muscular durante actividad física de alta densidad; favorece el manejo de fibromialgia y fatiga crónica; apoya la función cardíaca en casos de insuficiencia.",
            "otros": "**Dósis Sugerida:** 1-2 Scoops (2 gr c/u).\n**Contenido:** 200 gr.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado en casos de gota o hiperuricemia. Consultar con médico en diabetes.",
        "promocionados": [14, 19, 22],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 17,
        "codigo": "17953-ALE-0122",
        "disponible": true,
        "nombre": "MULTIESSENS MINERALES",
        "precio": 22,
        "imagen": "assets/productos/multiessens-minerales.png",
        "logo": "assets/logos/multiessens-minerales.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Minerales Esenciales biodisponibles.",
        "categoria": ["Vitaminas y Minerales"],
        "descripciones": {
            "principal": "Activan, equilibran y regulan procesos bioquímicos; contribuyen al metabolismo de nutrientes; actúan como potente antioxidante.",
            "otros": "**Dósis Sugerida:** 3 Tabletas.\n**Contenido:** 60 tabletas.\n**Tiempo del tratamiento:** 20 Días."
        },
        "contraindicaciones": "No recomendado en casos de insuficiencia renal. Consultar con médico en condiciones de sobrecarga mineral.",
        "promocionados": [18, 12, 13],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 18,
        "codigo": "18176-ALE-0122",
        "disponible": true,
        "nombre": "MULTIESSENS VITAMINAS",
        "precio": 33,
        "imagen": "assets/productos/multiessens-vitaminas.png",
        "logo": "assets/logos/multiessens-vitaminas.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Vitaminas Esenciales en sus formas activas, B9 (L-5-Metiltetrahidrofolato) y complejos de bioflavonoides y polifenoles derivados de frutas y plantas.",
        "categoria": ["Vitaminas y Minerales"],
        "descripciones": {
            "principal": "Aporta un espectro completo de vitaminas para una nutrición celular óptima; refuerza procesos metabólicos, inmunitarios y antioxidantes; favorece el mantenimiento del bienestar general.",
            "otros": "**Dósis Sugerida:** 3 Cápsulas.\n**Contenido:** 60 cápsulas.\n**Tiempo del tratamiento:** 20 Días."
        },
        "contraindicaciones": "No recomendado en casos de hipervitaminosis. Consultar con médico durante embarazo y lactancia.",
        "promocionados": [17, 12, 13],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 19,
        "codigo": "6374-ALE-0518",
        "disponible": true,
        "nombre": "MYOESSENS",
        "precio": 30,
        "imagen": "assets/productos/myoessens.png",
        "logo": "assets/logos/myoessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Magnesio y Potasio.",
        "categoria": ["Muscular", "Digestión", "Energía", "Dolor"],
        "descripciones": {
            "principal": "Alivia la tensión muscular, incluidos calambres; contribuye al alivio de dolores de cabeza y migrañas; facilita el tránsito intestinal y alivia el dolor menstrual; ayuda a mantener la presión arterial.",
            "otros": "**Dósis Sugerida:** 3 Cápsulas.\n**Contenido:** 60 cápsulas.\n**Tiempo del tratamiento:** 20 Días."
        },
        "contraindicaciones": "No recomendado en casos de insuficiencia renal severa. Consultar con médico si se toman diuréticos.",
        "promocionados": [7, 11, 16],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 20,
        "codigo": "10831-ALE-0719",
        "disponible": true,
        "nombre": "NEURESSENS",
        "precio": 45,
        "imagen": "assets/productos/neuressens.png",
        "logo": "assets/logos/neuressens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Metilcobalamina (B12), Adenosilcobalamina (B12), Piridoxal-5-Fosfato (B6) y Tiamina (B1).",
        "categoria": ["Sistema Nervioso", "Muscular", "Dolor"],
        "descripciones": {
            "principal": "Previene y corrige la deficiencia de vitamina B12 evitando las molestias de las inyecciones; coadyuvante en el alivio de dolor muscular y neurítico; apoya la recuperación nutricional tras gastrectomía y bypass gástrico.",
            "otros": "**Dósis Sugerida:** 0.5 ml.\n**Contenido:** 30 ml.\n**Tiempo del tratamiento:** 60 Días."
        },
        "contraindicaciones": "No recomendado en casos de hipersensibilidad a cobalaminas. Consultar con médico en neuropatías.",
        "promocionados": [1, 3, 25],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 21,
        "codigo": "11079-ALE-0819",
        "disponible": true,
        "nombre": "OMEGAESSENS",
        "precio": 49.5,
        "imagen": "assets/productos/omegaessens.png",
        "logo": "assets/logos/omegaessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Aceite de Krill, Omega-3 (EPA y DHA) y Fosfolípidos.",
        "categoria": ["Salud Cardiovascular", "Colesterol", "Embarazo y Lactancia"],
        "descripciones": {
            "principal": "EPA: mejora la salud neurológica, la integridad de las articulaciones y la salud cardiovascular; DHA: favorece el desarrollo óptimo del feto y la función neurológica; aporta acción antiinflamatoria y cardioprotectora.",
            "otros": "**Dósis Sugerida:** 2-3 Cápsulas al día.\n**Contenido:** 60 cápsulas.\n**Tiempo del tratamiento:** 20 Días."
        },
        "contraindicaciones": "No recomendado en casos de alergia a mariscos. Consultar con médico si se toman anticoagulantes.",
        "promocionados": [5, 25, 4],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 22,
        "codigo": "10846-ALE-0819",
        "disponible": true,
        "nombre": "PANCREOGEN",
        "precio": 38,
        "imagen": "assets/productos/pancreogen.png",
        "logo": "assets/logos/pancreogen.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Páncreas Bovino Liofilizado, Vitaminas y Minerales Esenciales.",
        "categoria": ["Metabolismo"],
        "descripciones": {
            "principal": "Coadyuvante en regulación de azúcar en sangre y coadyuva en el manejo del síndrome metabólico y la diabetes. No presenta contraindicaciones ni interacciones con medicamentos.",
            "otros": "**Dósis Sugerida:** 3 Cápsulas.\n**Contenido:** 90 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado para personas con alergia a productos bovinos. Consultar con médico en diabetes tipo 1.",
        "promocionados": [14, 16, 25],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 23,
        "codigo": "NSOC79899-17CO",
        "disponible": true,
        "nombre": "P-CIDE",
        "precio": 31,
        "imagen": "assets/productos/p-cide.png",
        "logo": "assets/logos/p-cide.png",
        "descripcion": "**Aceite Esencial - NUTRABIOTICS®** \nFórmula con 10 Aceites Esenciales orgánicos extraídos por arrastre de vapor.",
        "categoria": ["Aceites Esenciales"],
        "descripciones": {
            "principal": "Propiedades antiparasitarias y antibacterianas que favorecen la higiene natural de la piel y la purificación de ambientes.",
            "otros": "**Dósis Sugerida:** 5 Gotas diarias (diluidas en aceite de oliva o coco).\n**Contenido:** 9 ml.\n**Tiempo del tratamiento:** 21 Días."
        },
        "contraindicaciones": "No aplicar en piel lesionada. Evitar contacto con ojos. No ingerir. Mantener fuera del alcance de niños.",
        "promocionados": [26, 27],
        "descuento": 10,
        "disclaimer": "Este producto es un cosmético, cumple con todos los requisitos establecidos por la Decisión 833 de la Comunidad Andina, **Consulte con su profesional de salud.**."
    },
    {
        "id": 24,
        "codigo": "8169-ALE-0818",
        "disponible": true,
        "nombre": "PROBIOESSENS",
        "precio": 18.5,
        "imagen": "assets/productos/probioessens.png",
        "logo": "assets/logos/probioessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Lactobacillus Casei R215 ND, Lactobacillus Rhamnosus R11 ND, Lactobacillus Helveticus Lafti® L10 ND, Bifidobacterium Animalis Lactis Lafti® B94, Vitamina C y Selenio.",
        "categoria": ["Digestión"],
        "descripciones": {
            "principal": "Repobla y estabiliza la microflora intestinal tras tratamientos con antibióticos, fortalece el sistema inmunológico, mejora la digestión, alivia la intolerancia a la lactosa y reduce el crecimiento de bacterias patógenas.",
            "otros": "**Dósis Sugerida:** 1 Sobre.\n**Contenido:** 10 sobres.\n**Tiempo del tratamiento:** 10 Días."
        },
        "contraindicaciones": "No recomendado en casos de inmunosupresión severa. Consultar con médico en SIBO.",
        "promocionados": [2, 8, 28],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 25,
        "codigo": "14949-ALE-0221",
        "disponible": true,
        "nombre": "QUINTESSENS",
        "precio": 56.5,
        "imagen": "assets/productos/quintessens.png",
        "logo": "assets/logos/quintessens.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con Coenzima Q10 (Ubiquinona), Riboflavina (B2) y Tiamina (B1).",
        "categoria": ["Antioxidantes", "Energía"],
        "descripciones": {
            "principal": "Apoya la salud cardiovascular y el manejo del síndrome metabólico; coadyuva en la prevención de miopatías por estatinas y procesos de neurodegeneración; contribuye a la profilaxis de migrañas y favorece la función reproductiva masculina.",
            "otros": "**Dósis Sugerida:** 2 Cápsulas.\n**Contenido:** 60 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado en casos de hipotensión. Consultar con médico si se toman anticoagulantes.",
        "promocionados": [5, 21, 4],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    },
    {
        "id": 26,
        "codigo": "NSOC00222-20CO",
        "disponible": true,
        "nombre": "S-CIDE",
        "precio": 11,
        "imagen": "assets/productos/s-cide.png",
        "logo": "assets/logos/s-cide.png",
        "descripcion": "**Aceite Esencial - NUTRABIOTICS®** \nFórmula con Propóleo, Jengibre, Canela, Menta Piperita y Aceites Esenciales orgánicos.",
        "categoria": ["Aceites Esenciales"],
        "descripciones": {
            "principal": "Aerosol bucal refrescante con efecto antiviral, antibacteriano, antifúngico y antiparasitario; acción inmunomoduladora y antioxidante; garantiza la desinfección y purificación de la cavidad oral.",
            "otros": "**Dósis Sugerida:** 2 Puffs.\n**Contenido:** 20 ml.\n**Tiempo del tratamiento:** 20 Días."
        },
        "contraindicaciones": "No recomendado en casos de alergia a productos apícolas. Evitar en niños menores de 6 años.",
        "promocionados": [23, 27],
        "descuento": 10,
        "disclaimer": "Este producto es un cosmético, cumple con todos los requisitos establecidos por la Decisión 833 de la Comunidad Andina, **Consulte con su profesional de salud.**"
    },
    {
        "id": 27,
        "codigo": "NSOC80055-17CO",
        "disponible": true,
        "nombre": "V-CIDE",
        "precio": 31,
        "imagen": "assets/productos/v-cide.png",
        "logo": "assets/logos/v-cide.png",
        "descripcion": "**Aceite Esencial - NUTRABIOTICS®** \nFórmula con 8 Aceites Esenciales orgánicos extraídos por arrastre de vapor.",
        "categoria": ["Aceites Esenciales"],
        "descripciones": {
            "principal": "Propiedades antivirales y antifúngicas que favorecen la higiene natural de la piel y la purificación de ambientes.",
            "otros": "**Dósis Sugerida:** 5 Gotas diarias (diluidas en aceite de oliva o coco).\n**Contenido:** 9 ml.\n**Tiempo del tratamiento:** 21 Días."
        },
        "contraindicaciones": "No aplicar en piel lesionada. Evitar contacto con ojos. No ingerir. Mantener fuera del alcance de niños.",
        "promocionados": [23, 26],
        "descuento": 10,
        "disclaimer": "Este producto es un cosmético, cumple con todos los requisitos establecidos por la Decisión 833 de la Comunidad Andina, **Consulte con su profesional de salud.**"
    },
    {
        "id": 28,
        "codigo": "12456-ALE-0220",
        "disponible": true,
        "nombre": "VEGEZYM",
        "precio": 50.5,
        "imagen": "assets/productos/vegezym.png",
        "logo": "assets/logos/vegezym.png",
        "descripcion": "**Suplemento Alimenticio - NUTRABIOTICS®** \nFórmula con 10 Enzimas Digestivas 100% veganas, Cofactores Esenciales, Minerales Esenciales y Vitamina B3.",
        "categoria": ["Digestión"],
        "descripciones": {
            "principal": "Otorga un alivio efectivo e inmediato de las molestias gástricas, promueve la buena digestión y reduce el riesgo de infecciones gástricas; 100% vegano.",
            "otros": "**Dósis Sugerida:** 3 Cápsulas.\n**Contenido:** 90 cápsulas.\n**Tiempo del tratamiento:** 30 Días."
        },
        "contraindicaciones": "No recomendado en pacientes con alergia a piña y papaya. Consultar con médico en úlceras gástricas activas.",
        "promocionados": [2, 9, 24],
        "descuento": 10,
        "disclaimer": "El producto no es adecuado para ser consumido como única fuente de alimento. No supera la dosis recomendada, **Consulte con su profesional de salud.**"
    }
]';

    /* 2) Parsear JSON → tabla temp #S */
    IF OBJECT_ID('tempdb..#S') IS NOT NULL DROP TABLE #S;

    CREATE TABLE #S (
        NombreKey          NVARCHAR(300) NOT NULL,
        Nombre             NVARCHAR(300) NOT NULL,
        Codigo             NVARCHAR(50)  NULL,
        Disponible         BIT           NULL,
        Precio             DECIMAL(18,2) NULL,
        Imagen             NVARCHAR(255) NULL,
        Logo               NVARCHAR(255) NULL,
        Descripcion        NVARCHAR(MAX) NULL,
        Desc_Principal     NVARCHAR(MAX) NULL,
        Desc_Otros         NVARCHAR(MAX) NULL,
        Descuento          INT           NULL,
        Disclaimer         NVARCHAR(MAX) NULL,
        Contraindicaciones NVARCHAR(MAX) NULL
    );

    INSERT INTO #S
    SELECT
        UPPER(JSON_VALUE(j.value,'$.nombre'))              AS NombreKey,
        JSON_VALUE(j.value,'$.nombre')                     AS Nombre,
        JSON_VALUE(j.value,'$.codigo')                     AS Codigo,
        CASE WHEN JSON_VALUE(j.value,'$.disponible')='true' THEN 1 ELSE 0 END AS Disponible,
        TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(j.value,'$.precio')) AS Precio,
        JSON_VALUE(j.value,'$.imagen')                     AS Imagen,
        JSON_VALUE(j.value,'$.logo')                       AS Logo,
        JSON_VALUE(j.value,'$.descripcion')                AS Descripcion,
        JSON_VALUE(j.value,'$.descripciones.principal')    AS Desc_Principal,
        JSON_VALUE(j.value,'$.descripciones.otros')        AS Desc_Otros,
        TRY_CONVERT(INT, JSON_VALUE(j.value,'$.descuento')) AS Descuento,
        JSON_VALUE(j.value,'$.disclaimer')                 AS Disclaimer,
        JSON_VALUE(j.value,'$.contraindicaciones')         AS Contraindicaciones
    FROM OPENJSON(@json) AS j;

    /* 3) UPDATE existentes por nombre (case-insensitive) */
    UPDATE p
    SET
        p.Codigo             = s.Codigo,
        p.Disponible         = s.Disponible,
        p.Nombre             = s.Nombre,
        p.Precio             = s.Precio,
        p.Imagen             = s.Imagen,
        p.Logo               = s.Logo,
        p.Descripcion        = s.Descripcion,
        p.Desc_Principal     = s.Desc_Principal,
        p.Desc_Otros         = s.Desc_Otros,
        p.Descuento          = ISNULL(s.Descuento, p.Descuento),
        p.Disclaimer         = s.Disclaimer,
        p.Contraindicaciones = s.Contraindicaciones,
        p.ActualizadoEl      = SYSUTCDATETIME(),
        p.Activo             = 1
    FROM dbo.Producto p
    JOIN #S s
      ON UPPER(p.Nombre) = s.NombreKey;

    /* 4) INSERT nuevos */
    INSERT INTO dbo.Producto
        (Codigo, Disponible, Nombre, Precio, Imagen, Logo,
         Descripcion, Desc_Principal, Desc_Otros, Descuento, Disclaimer,
         Activo, CreadoEl, ActualizadoEl, Stock, StockReservado, Contraindicaciones)
    SELECT
        s.Codigo,
        s.Disponible,
        s.Nombre,
        s.Precio,
        s.Imagen,
        s.Logo,
        s.Descripcion,
        s.Desc_Principal,
        s.Desc_Otros,
        ISNULL(s.Descuento, 0),
        s.Disclaimer,
        1,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        0, 0,
        s.Contraindicaciones
    FROM #S s
    LEFT JOIN dbo.Producto p
      ON UPPER(p.Nombre) = s.NombreKey
    WHERE p.Id IS NULL;

    COMMIT TRAN;
    PRINT 'Productos actualizados e insertados desde JSON. Lechuguin/Ejemplo desactivados.';
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
