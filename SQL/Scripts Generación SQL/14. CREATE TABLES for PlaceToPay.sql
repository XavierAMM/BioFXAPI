-- =============================================
-- TABLA ORDERS (ÓRDENES) - AJUSTADA A TU ESQUEMA
-- =============================================
CREATE TABLE [dbo].[Orders](
    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [UserId] [int] NOT NULL FOREIGN KEY REFERENCES [dbo].[Usuario]([id]),
    [OrderNumber] [nvarchar](50) UNIQUE NOT NULL,
    
    -- CAMPOS REQUERIDOS POR PLACETOPAY (doc 3. Crear Sesión.pdf)
    [Reference] [nvarchar](32) NOT NULL, -- Referencia única para PlacetoPay (hasta 32 chars)
    [Description] [nvarchar](250) NULL, -- Descripción del pago (hasta 250 chars)
    
    [TotalAmount] [decimal](18,2) NOT NULL,
    [TaxAmount] [decimal](18,2) NOT NULL, -- IVA 15%
    [Currency] [nvarchar](3) NOT NULL DEFAULT 'USD',
    
    -- ESTADOS DEL NEGOCIO (no confundir con estados de PlacetoPay)
    [Status] [nvarchar](20) NOT NULL DEFAULT 'Pending', -- 'Pending', 'Paid', 'Failed', 'Expired'
    
    [Activo] [bit] NOT NULL DEFAULT 1,
    [CreadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [ActualizadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE()
);

-- =============================================
-- TABLA ORDER_ITEMS - AJUSTADA A TU ESQUEMA
-- =============================================
CREATE TABLE [dbo].[OrderItems](
    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [OrderId] [int] NOT NULL FOREIGN KEY REFERENCES [dbo].[Orders]([Id]) ON DELETE CASCADE,
    [ProductId] [int] NOT NULL FOREIGN KEY REFERENCES [dbo].[Producto]([Id]),
    [Quantity] [int] NOT NULL,
    [UnitPrice] [decimal](18,2) NOT NULL,
    [TotalPrice] [decimal](18,2) NOT NULL,
    [Activo] [bit] NOT NULL DEFAULT 1,
    [CreadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE()
);

-- =============================================
-- TABLA TRANSACTIONS - CRÍTICA PARA PLACETOPAY
-- =============================================
CREATE TABLE [dbo].[Transactions](
    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [OrderId] [int] NOT NULL FOREIGN KEY REFERENCES [dbo].[Orders]([Id]),
    
    -- DATOS DE SESIÓN PLACETOPAY (doc 19. API Pagos.pdf)
    [RequestId] [int] NOT NULL, -- ID de sesión de PlacetoPay
    [InternalReference] [int] NULL, -- Referencia interna de PlacetoPay (para reembolsos)
    [ProcessUrl] [nvarchar](500) NOT NULL, -- URL a redirigir al usuario
    
    -- ESTADOS DE PLACETOPAY (doc 3. Crear Sesión.pdf, Página 4)
    [Status] [nvarchar](50) NOT NULL DEFAULT 'PENDING', -- 'PENDING', 'APPROVED', 'REJECTED', 'APPROVED_PARTIAL', 'PARTIAL_EXPIRED'
    [Reason] [nvarchar](10) NULL, -- Código de razón (ej: '00' para éxito)
    [Message] [nvarchar](255) NULL, -- Mensaje de estado
    
    -- INFORMACIÓN DEL PAGO (doc 19. API Pagos.pdf, Página 3)
    [PaymentMethod] [nvarchar](50) NULL, -- visa, master, etc.
    [PaymentMethodName] [nvarchar](100) NULL, -- nombre del método
    [IssuerName] [nvarchar](255) NULL, -- banco emisor
    
    -- REEMBOLSOS (doc 10. Reembolsos.pdf)
    [Refunded] [bit] NOT NULL DEFAULT 0,
    [RefundedAmount] [decimal](18,2) NULL,
    
    [Activo] [bit] NOT NULL DEFAULT 1,
    [CreadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [ActualizadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE()
);

-- =============================================
-- TABLA WEBHOOK_LOGS - PARA NOTIFICACIONES
-- =============================================
CREATE TABLE [dbo].[WebhookLogs](
    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [RequestId] [int] NOT NULL,
    [Payload] [nvarchar](MAX) NOT NULL, -- JSON completo recibido
    [Signature] [nvarchar](255) NOT NULL, -- Firma para verificación (doc 4. Notificación.pdf)
    [Status] [nvarchar](50) NOT NULL,
    [Processed] [bit] NOT NULL DEFAULT 0,
    [Activo] [bit] NOT NULL DEFAULT 1,
    [CreadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [ActualizadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE()
);

-- =============================================
-- TABLAS DE CARRITO - PERSISTENCIA PROFESIONAL
-- =============================================
CREATE TABLE [dbo].[ShoppingCarts](
    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [UserId] [int] NOT NULL FOREIGN KEY REFERENCES [dbo].[Usuario]([id]),
    [Activo] [bit] NOT NULL DEFAULT 1,
    [CreadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [ActualizadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE()
);

CREATE TABLE [dbo].[CartItems](
    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [CartId] [int] NOT NULL FOREIGN KEY REFERENCES [dbo].[ShoppingCarts]([Id]) ON DELETE CASCADE,
    [ProductId] [int] NOT NULL FOREIGN KEY REFERENCES [dbo].[Producto]([Id]),
    [Quantity] [int] NOT NULL CHECK (Quantity > 0),
    [Activo] [bit] NOT NULL DEFAULT 1,
    [AgregadoEl] [datetime2](7) NOT NULL DEFAULT GETUTCDATE()
);

-- =============================================
-- ÍNDICES PARA OPTIMIZACIÓN
-- =============================================
CREATE INDEX IX_Orders_UserId ON [dbo].[Orders]([UserId]);
CREATE INDEX IX_Orders_Reference ON [dbo].[Orders]([Reference]);
CREATE INDEX IX_Transactions_RequestId ON [dbo].[Transactions]([RequestId]);
CREATE INDEX IX_Transactions_InternalReference ON [dbo].[Transactions]([InternalReference]);
CREATE INDEX IX_Transactions_OrderId ON [dbo].[Transactions]([OrderId]);
CREATE INDEX IX_WebhookLogs_RequestId ON [dbo].[WebhookLogs]([RequestId]);
CREATE INDEX IX_ShoppingCarts_UserId ON [dbo].[ShoppingCarts]([UserId]);
CREATE INDEX IX_CartItems_CartId ON [dbo].[CartItems]([CartId]);
CREATE INDEX IX_CartItems_ProductId ON [dbo].[CartItems]([ProductId]);