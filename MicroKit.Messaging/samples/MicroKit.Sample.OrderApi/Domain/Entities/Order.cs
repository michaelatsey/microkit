namespace MicroKit.Sample.OrderApi.Domain.Entities
{
    public class Order
    {
        public Guid Id { get; private set; }
        public long ProductId { get; private  set; }
        public DateTime OrderDate { get; private set; }
        public decimal TotalAmount { get; private set; }

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

        public static Order Create(long productId, decimal totalAmount)
        {
            return new Order(Guid.NewGuid(), productId, DateTime.UtcNow, totalAmount);
        }
    }
}
