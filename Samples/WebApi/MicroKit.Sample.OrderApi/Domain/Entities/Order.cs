namespace MicroKit.Sample.OrderApi.Domain.Entities
{
    /// <summary>Represents an order in the sample application.</summary>
    public class Order
    {
        /// <summary>Gets the unique order identifier.</summary>
        public Guid Id { get; private set; }
        /// <summary>Gets the identifier of the ordered product.</summary>
        public long ProductId { get; private set; }
        /// <summary>Gets the date the order was placed.</summary>
        public DateTime OrderDate { get; private set; }
        /// <summary>Gets the total amount of the order.</summary>
        public decimal TotalAmount { get; private set; }

        /// <summary>Initializes a new instance for EF Core.</summary>
        protected Order()
        {
        }
        private Order(Guid id, long productId, DateTime orderDate, decimal totalAmount)
        {
            Id = id;
            ProductId = productId;
            OrderDate = orderDate;
            TotalAmount = totalAmount;

        }

        /// <summary>Creates a new order with a generated identifier and the current UTC timestamp.</summary>
        /// <param name="productId">The identifier of the ordered product.</param>
        /// <param name="totalAmount">The total amount of the order.</param>
        /// <returns>A new <see cref="Order"/> instance.</returns>
        public static Order Create(long productId, decimal totalAmount)
        {
            return new Order(Guid.NewGuid(), productId, DateTime.UtcNow, totalAmount);
        }
    }
}
