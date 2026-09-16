using System.Runtime.Serialization;

namespace IntraBox.Core
{
    /// <summary>护眼一天中的一段：起止时刻与色温亮度。</summary>
    [DataContract]
    public sealed class EyeCareSegment
    {
        [DataMember]
        public string StartHm { get; set; }

        [DataMember]
        public string EndHm { get; set; }

        [DataMember]
        public int Kelvin { get; set; }

        [DataMember]
        public int Brightness { get; set; }

        /// <summary>为 true 时本段不生效。缺字段为 false，即默认开启。</summary>
        [DataMember]
        public bool Disabled { get; set; }

        public EyeCareSegment Clone()
        {
            return new EyeCareSegment
            {
                StartHm = StartHm,
                EndHm = EndHm,
                Kelvin = Kelvin,
                Brightness = Brightness,
                Disabled = Disabled
            };
        }
    }
}
