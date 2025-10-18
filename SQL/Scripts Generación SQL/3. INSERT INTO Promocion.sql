-- Inserción de promociones desde el JSON

INSERT INTO Promocion (
    titulo, descripcion, botonTexto, botonUrl, imagen, 
    textoAlineacionId, imagenAlineacionId, fondoId, colorTexto
)
VALUES
(
    'Oferta Especial de Verano',
    'Hasta 50% de descuento en productos seleccionados de nuestra línea de concentración',
    'Ver Ofertas',
    '#ofertas',
    'https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?ixlib=rb-4.0.3&ixid=M3wxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8fA%3D%3D&auto=format&fit=crop&w=800&q=80',
    (SELECT id FROM Alineacion WHERE descripcion = 'izquierda'),
    (SELECT id FROM Alineacion WHERE descripcion = 'derecha'),
    (SELECT id FROM Fondo WHERE descripcion = 'gradiente'),
    '#ffffff'
),
(
    'Nuevo Lanzamiento',
    'Descubre nuestro nuevo producto para mejorar la concentración con ingredientes 100% naturales',
    'Descubrir',
    '#productos',
    'https://images.unsplash.com/photo-1559757148-5c350d0d3c56?ixlib=rb-4.0.3&ixid=M3wxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8fA%3D%3D&auto=format&fit=crop&w=800&q=80',
    (SELECT id FROM Alineacion WHERE descripcion = 'derecha'),
    (SELECT id FROM Alineacion WHERE descripcion = 'izquierda'),
    (SELECT id FROM Fondo WHERE descripcion = 'gradiente'),
    '#ffffff'
),
(
    'Promoción Exclusiva',
    'Compra 2 productos y lleva el tercero con 30% de descuento en toda nuestra línea calmante',
    'Aprovechar Ahora',
    '#calmantes',
    'https://images.unsplash.com/photo-1582719468257-c8e7c6f329bc?ixlib=rb-4.0.3&ixid=M3wxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8fA%3D%3D&auto=format&fit=crop&w=800&q=80',
    (SELECT id FROM Alineacion WHERE descripcion = 'centro'),
    (SELECT id FROM Alineacion WHERE descripcion = 'centro'),
    (SELECT id FROM Fondo WHERE descripcion = 'gradiente'),
    '#ffffff'
);
