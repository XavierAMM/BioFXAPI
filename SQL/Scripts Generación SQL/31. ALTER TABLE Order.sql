-- 1) Añadir columnas nuevas
ALTER TABLE [dbo].[Order]
ADD
    [OrderAttachmentId] INT NULL,
    [DocumentType]      NVARCHAR(20)  NOT NULL CONSTRAINT [DF_Order_DocumentType]      DEFAULT(''),
    [DocumentNumber]    NVARCHAR(50)  NOT NULL CONSTRAINT [DF_Order_DocumentNumber]    DEFAULT(''),
    [AddressLine]       NVARCHAR(255) NOT NULL CONSTRAINT [DF_Order_AddressLine]       DEFAULT(''),
    [City]              NVARCHAR(100) NOT NULL CONSTRAINT [DF_Order_City]              DEFAULT(''),
    [Province]          NVARCHAR(100) NOT NULL CONSTRAINT [DF_Order_Province]          DEFAULT(''),
    [PostalCode]        NVARCHAR(20)  NULL,
    [Country]           NVARCHAR(100) NOT NULL CONSTRAINT [DF_Order_Country]           DEFAULT(''),
    [DoctorName]        NVARCHAR(150) NULL;
GO

-- 2) Añadir FK hacia OrderAttachment
ALTER TABLE [dbo].[Order]
ADD CONSTRAINT [FK_Order_OrderAttachment]
    FOREIGN KEY ([OrderAttachmentId])
    REFERENCES [dbo].[OrderAttachment]([Id]);
GO
