﻿#region File and License Information
/*
<File>
	<License>
		Copyright © 2009 - 2017, Daniel Vaughan. All rights reserved.
		This file is part of Codon (http://codonfx.com), 
		which is released under the MIT License.
		See file /Documentation/License.txt for details.
	</License>
	<CreationDate>$CreationDate$</CreationDate>
</File>
*/
#endregion

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Codon.ComponentModel;

namespace Codon.Collections
{
	/// <summary>
	/// An adaptive collection allows you to combine two different collection types.
	/// It allows you to materialize the objects in the inner collection,
	/// which are of type <c>TInner</c> as objects of type <c>T</c>
	/// using a <see cref="IAttachObject{T}"/> implementation.
	/// This class is useful, for example, in materializing a collection 
	/// of <c>ICommand</c> objects as a set of <c>DropDownMenuItem</c> objects.
	/// </summary>
	/// <typeparam name="T">
	/// The type of objects that can be bound to a list.
	/// </typeparam>
	/// <typeparam name="TInner">
	/// The type of objects contained in the source collection.</typeparam>
	public class AdaptiveCollection<T, TInner> : ObservableCollection<T>, 
		IDisposable
		where T : class, IAttachObject<TInner>, new()
	{
		readonly Dictionary<TInner, T> lookupDictionary
			= new Dictionary<TInner, T>();

		readonly ObservableCollection<TInner> monitoredCollection;
		public ObservableCollection<TInner> MonitoredCollection => monitoredCollection;

		bool ignoreChanges;

		public AdaptiveCollection(ObservableCollection<TInner> monitoredCollection)
		{
			AssertArg.IsNotNull(monitoredCollection, nameof(monitoredCollection));

			this.monitoredCollection = monitoredCollection;
			monitoredCollection.CollectionChanged += OnCollectionChanged;

			try
			{
				ignoreChanges = true;

				foreach (var command in monitoredCollection)
				{
					var attacher = new T();
					attacher.AttachObject(command);
					lookupDictionary[command] = attacher;

					base.Add(attacher);
				}
			}
			finally
			{
				ignoreChanges = false;
			}
		}

		void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (ignoreChanges)
			{
				return;
			}

			NotifyCollectionChangedAction action = e.Action;

			if (action == NotifyCollectionChangedAction.Add)
			{
				var list = new List<T>();

				foreach (TInner command in e.NewItems)
				{
					var attacher = new T();
					attacher.AttachObject(command);
					lookupDictionary[command] = attacher;
					list.Add(attacher);
				}

				AddRangeInternal(list);
			}
			else if (action == NotifyCollectionChangedAction.Move)
			{
				int movedFromIndex = e.OldStartingIndex;
				int movedToIndex = e.NewStartingIndex;

				try
				{
					ignoreChanges = true;
					base.MoveItem(movedFromIndex, movedToIndex);
				}
				finally
				{
					ignoreChanges = false;
				}

			}
			else if (action == NotifyCollectionChangedAction.Remove)
			{
				var oldItems = e.OldItems;

				var itemsToRemove = new List<T>();

				foreach (TInner oldItem in oldItems)
				{
					T owner;

					if (lookupDictionary.TryGetValue(oldItem, out owner) && owner != null)
					{
						itemsToRemove.Add(owner);
					}

					lookupDictionary.Remove(oldItem);
				}

				foreach (var owner in itemsToRemove)
				{
					try
					{
						ignoreChanges = true;
						base.Remove(owner);
					}
					finally
					{
						ignoreChanges = false;
					}

					owner.DetachObject();

					var disposable = owner as IDisposable;
					disposable?.Dispose();
				}
			}
			else if (action == NotifyCollectionChangedAction.Replace)
			{
				var removedItems = e.OldItems;

				var itemsToRemove = new List<T>();

				foreach (TInner oldItem in removedItems)
				{
					T owner;

					if (lookupDictionary.TryGetValue(oldItem, out owner) && owner != null)
					{
						itemsToRemove.Add(owner);
					}

					lookupDictionary.Remove(oldItem);
				}

				foreach (var owner in itemsToRemove)
				{
					try
					{
						ignoreChanges = true;
						base.Remove(owner);
					}
					finally
					{
						ignoreChanges = false;
					}

					owner.DetachObject();

					var disposable = owner as IDisposable;
					disposable?.Dispose();
				}

				var newItems = e.NewItems;
				foreach (TInner command in newItems)
				{
					var index = monitoredCollection.IndexOf(command);

					var attacher = new T();
					attacher.AttachObject(command);
					lookupDictionary[command] = attacher;

					try
					{
						ignoreChanges = true;
						base.Insert(index, attacher);
					}
					finally
					{
						ignoreChanges = false;
					}
				}
			}
			else if (action == NotifyCollectionChangedAction.Reset)
			{
				var list = new List<T>(Items);

				try
				{
					ignoreChanges = true;
					base.Clear();
				}
				finally
				{
					ignoreChanges = false;
				}

				foreach (var owner in list)
				{
					if (owner == null)
					{
						continue;
					}

					owner.DetachObject();

					var disposable = owner as IDisposable;
					disposable?.Dispose();
				}

				lookupDictionary.Clear();

				var tempList = new List<T>();
				foreach (var command in monitoredCollection)
				{
					var attacher = new T();
					attacher.AttachObject(command);
					lookupDictionary[command] = attacher;

					tempList.Add(attacher);
				}

				try
				{
					ignoreChanges = true;
					foreach (var item in tempList)
					{
						base.Add(item);
					}
				}
				finally
				{
					ignoreChanges = false;
				}

			}
		}

		public void AddRange(IEnumerable<T> range)
		{
			// ReSharper disable PossibleMultipleEnumeration
			AssertArg.IsNotNull(range, nameof(range));


			foreach (var item in range)
			{
				Add(item);
			}
			// ReSharper restore PossibleMultipleEnumeration
		}

		void AddRangeInternal(IEnumerable<T> range)
		{
			try
			{
				ignoreChanges = true;

				foreach (var item in range)
				{
					base.InsertItem(Count, item);
				}
			}
			finally
			{
				ignoreChanges = false;
			}
		}

		public new void Add(T item)
		{
			AssertArg.IsNotNull(item, nameof(item));

			TInner obj = item.GetObject();
			if (obj == null)
			{
				throw new Exception("Item must have an existing attached " +
									"object to insert into an AdaptiveCollection.");
			}

			try
			{
				ignoreChanges = true;

				monitoredCollection.Add(obj);
				base.InsertItem(base.Count, item);
			}
			finally
			{
				ignoreChanges = false;
			}


		}

		protected override void InsertItem(int index, T item)
		{
			AssertArg.IsNotNull(item, nameof(item));

			if (ignoreChanges)
			{
				base.InsertItem(index, item);
				return;
			}

			TInner obj = item.GetObject();
			if (obj == null)
			{
				throw new Exception("Item must have an existing attached object " +
									"to insert into an AdaptiveCollection.");
			}

			try
			{
				ignoreChanges = true;

				monitoredCollection.Insert(index, obj);
			}
			finally
			{
				ignoreChanges = false;
			}

			base.InsertItem(index, item);

		}

		protected override void ClearItems()
		{
			if (ignoreChanges)
			{
				base.ClearItems();
				return;
			}

			try
			{
				ignoreChanges = true;

				monitoredCollection.Clear();
			}
			finally
			{
				ignoreChanges = false;
			}

			base.ClearItems();
		}

		protected override void MoveItem(int oldIndex, int newIndex)
		{
			if (ignoreChanges)
			{
				base.MoveItem(oldIndex, newIndex);
				return;
			}

			monitoredCollection.Move(oldIndex, newIndex);
		}

		protected override void RemoveItem(int index)
		{
			if (ignoreChanges)
			{
				base.RemoveItem(index);
				return;
			}

			monitoredCollection.RemoveAt(index);
		}

		protected override void SetItem(int index, T item)
		{
			if (ignoreChanges)
			{
				base.SetItem(index, item);
				return;
			}

			TInner obj = item.GetObject();
			if (obj == null)
			{
				throw new Exception("Item must have an existing attached " +
									"object to insert into an AdaptiveCollection.");
			}

			try
			{
				ignoreChanges = true;

				monitoredCollection[index] = obj;
			}
			finally
			{
				ignoreChanges = false;
			}

			base.SetItem(index, item);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				/* Free managed resources. */

				var collection = monitoredCollection;
				if (collection != null)
				{
					collection.CollectionChanged -= OnCollectionChanged;
				}

				var lookup = lookupDictionary;
				lookup?.Clear();

				Clear();
			}

			/* Free native resources if there are any. */
		}
	}
}