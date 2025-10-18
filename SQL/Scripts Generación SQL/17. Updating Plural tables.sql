BEGIN TRAN;

-- CartItems -> CartItem
IF OBJECT_ID('dbo.CartItems','U') IS NOT NULL
AND OBJECT_ID('dbo.CartItem','U') IS NULL
    EXEC sp_rename 'dbo.CartItems', 'CartItem';

-- ShoppingCarts -> ShoppingCart
IF OBJECT_ID('dbo.ShoppingCarts','U') IS NOT NULL
AND OBJECT_ID('dbo.ShoppingCart','U') IS NULL
    EXEC sp_rename 'dbo.ShoppingCarts', 'ShoppingCart';

-- Orders -> [Order]  (reservada, usar corchetes)
IF OBJECT_ID('dbo.Orders','U') IS NOT NULL
AND OBJECT_ID('dbo.[Order]','U') IS NULL
    EXEC sp_rename 'dbo.Orders', 'Order';

-- OrderItems -> OrderItem
IF OBJECT_ID('dbo.OrderItems','U') IS NOT NULL
AND OBJECT_ID('dbo.OrderItem','U') IS NULL
    EXEC sp_rename 'dbo.OrderItems', 'OrderItem';

-- Transactions -> [Transaction]
IF OBJECT_ID('dbo.Transactions','U') IS NOT NULL
AND OBJECT_ID('dbo.[Transaction]','U') IS NULL
    EXEC sp_rename 'dbo.Transactions', 'Transaction';

-- WebhookLogs -> WebhookLog
IF OBJECT_ID('dbo.WebhookLogs','U') IS NOT NULL
AND OBJECT_ID('dbo.WebhookLog','U') IS NULL
    EXEC sp_rename 'dbo.WebhookLogs', 'WebhookLog';

COMMIT TRAN;
