using System;
using System.Linq;
using System.Reflection;
using FullSerializer;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private class LoadingFieldBase { public int inherited; }
		private sealed class LoadingFieldRecord : LoadingFieldBase
		{
			public int number;
			public int? optional;
			public string text;
			[fsProperty("secret")] private int _secret;
			public int Secret => _secret;
			public LoadingFieldRecord next;
			public LoadingFieldRecord shared;
		}
		private struct LoadingFieldValue { public int number; public string text; }
		private sealed class LoadingReadonlyField { public readonly int value = 5; }
		private sealed class LoadingFieldProperty
		{
			public int value { get; set; }
			[fsProperty] public int throws => throw new InvalidOperationException("native getter error");
		}
		private static FullSerializer.Internal.fsMetaProperty LoadingMember(Type type, string name) =>
			fsMetaType.Get(new fsConfig(), type).Properties.Single(p => p.MemberName == name);

		private void FieldLoadingChecks()
		{
			Check("field decoding retains inherited, private and nullable values", () =>
			{
				var serializer = new fsSerializer();
				LoadingFieldRecord record = null;
				var result = serializer.TryDeserialize(fsJsonParser.Parse("{\"number\":17,\"inherited\":29,\"optional\":31,\"secret\":47,\"text\":\"native\"}"), ref record);
				return (result.Succeeded && record.number == 17 && record.inherited == 29 && record.optional == 31
					&& record.Secret == 47 && record.text == "native", "field values and private JSON alias restored");
			});
			Check("writing a boxed struct mutates the original box", () =>
			{
				object box = new LoadingFieldValue { number = 5, text = "before" };
				LoadingMember(typeof(LoadingFieldValue), "number").Write(box, 23);
				LoadingMember(typeof(LoadingFieldValue), "text").Write(box, "after");
				var value = (LoadingFieldValue)box;
				return (value.number == 23 && value.text == "after", "boxed value-type identity retained, not a modified copy");
			});
			Check("native field coercion and null resets remain available", () =>
			{
				var record = new LoadingFieldRecord { number = 9, optional = 4, text = "old" };
				var number = LoadingMember(typeof(LoadingFieldRecord), "number");
				number.Write(record, (short)37);
				bool widened = record.number == 37;
				number.Write(record, null);
				LoadingMember(typeof(LoadingFieldRecord), "optional").Write(record, null);
				LoadingMember(typeof(LoadingFieldRecord), "text").Write(record, null);
				return (widened && record.number == 0 && record.optional == null && record.text == null,
					"native numeric widening and null-to-default assignments preserved");
			});
			Check("property access retains native getter exceptions", () =>
			{
				var record = new LoadingFieldProperty();
				var property = LoadingMember(typeof(LoadingFieldProperty), "value");
				property.Write(record, 13);
				bool assigned = (int)property.Read(record) == 13;
				try { LoadingMember(typeof(LoadingFieldProperty), "throws").Read(record); }
				catch (TargetInvocationException error)
				{
					return (assigned && error.InnerException is InvalidOperationException,
						"native property setter/getter and wrapped exception preserved");
				}
				return (false, "property getter did not preserve its native exception");
			});
			Check("field round-trip retains cycles and shared references", () =>
			{
				var record = new LoadingFieldRecord { number = 7 };
				var child = new LoadingFieldRecord { number = 19, next = record };
				record.next = record.shared = child;
				var serializer = new fsSerializer();
				serializer.TrySerialize(record, out var data).AssertSuccess();
				LoadingFieldRecord loaded = null;
				serializer.TryDeserialize(data, ref loaded).AssertSuccess();
				return (loaded.number == 7 && loaded.next.number == 19 && ReferenceEquals(loaded.next, loaded.shared)
					&& ReferenceEquals(loaded.next.next, loaded), "native graph cycles and alias identity survive conversion");
			});
			Check("missing saved fields preserve initialized state", () =>
			{
				var record = new LoadingFieldRecord { number = 5, text = "retained", optional = 11 };
				new fsSerializer().TryDeserialize(fsJsonParser.Parse("{\"number\":43}"), ref record).AssertSuccess();
				return (record.number == 43 && record.text == "retained" && record.optional == 11,
					"partial JSON does not overwrite omitted fields");
			});
			Check("readonly saved fields retain native assignment behavior", () =>
			{
				LoadingReadonlyField record = null;
				new fsSerializer().TryDeserialize(fsJsonParser.Parse("{\"value\":41}"), ref record).AssertSuccess();
				return (record.value == 41, "native reflection can restore readonly instance fields");
			});
		}
	}
}
