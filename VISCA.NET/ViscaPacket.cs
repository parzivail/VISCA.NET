using System;
using System.Threading;

namespace VISCA.NET
{
	public class ViscaPacket
	{
		public event EventHandler<ViscaAckStatus> Acknowledged;
		public event EventHandler<EventArgs> Completed;
		
		public ViscaDevice Device { get; }
		public readonly byte[] Data;

		private ViscaAckStatus _ackStatus = ViscaAckStatus.Waiting;
		private readonly ManualResetEventSlim _ackWaiter = new ManualResetEventSlim();
		private readonly ManualResetEventSlim _completionWaiter = new ManualResetEventSlim();

		public ViscaPacket(ViscaDevice device, byte[] data)
		{
			Device = device;
			Data = data;
		}

		public ViscaAckStatus WaitForAck()
		{
			_ackWaiter.Wait();
			return _ackStatus;
		}

		public void WaitForCompletion()
		{
			_completionWaiter.Wait();
		}

		internal void ConsumeAck(ViscaAckStatus ack)
		{
			_ackStatus = ack;
			_ackWaiter.Set();
			Acknowledged?.Invoke(this, ack);
		}

		internal void ConsumeCompleted()
		{
			_completionWaiter.Set();
			Completed?.Invoke(this, EventArgs.Empty);
		}
	}
}