namespace Dairy_Management_System.Models.Compensation
{
    public class FinalizeCompensationViewModel
    {
        public long CompensationId { get; set; }
        public int DeliveryBoyId { get; set; }
        public string DeliveryBoyName { get; set; } = string.Empty;
        public byte Month { get; set; }
        public short Year { get; set; }
        public decimal NetPayableAmount { get; set; }
    }
}
