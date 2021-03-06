﻿/* The MIT License (MIT)
* 
* Copyright (c) 2016 Marc Clifton
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;

namespace FlowSharpLib
{
	public abstract class BaseController
	{
        public const int MIN_WIDTH = 20;
        public const int MIN_HEIGHT = 20;

        public const int SNAP_ELEMENT_RANGE = 20;
		public const int SNAP_CONNECTION_POINT_RANGE = 10;
		public const int SNAP_DETACH_VELOCITY = 5;

		public const int CONNECTION_POINT_SIZE = 3;		// this is actually the length from center.

        public const int CAP_WIDTH = 5;
        public const int CAP_HEIGHT = 5;

		public Canvas Canvas { get { return canvas; } }

		protected List<GraphicElement> elements;
		public EventHandler<ElementEventArgs> ElementSelected;
		public EventHandler<ElementEventArgs> UpdateSelectedElement;

		public GraphicElement SelectedElement { get { return selectedElement; } }

		protected Canvas canvas;
		protected GraphicElement selectedElement;
		protected ShapeAnchor selectedAnchor;
		protected GraphicElement showingAnchorsElement;

		protected bool dragging;
		protected bool leftMouseDown;

		public BaseController(Canvas canvas, List<GraphicElement> elements)
		{
			this.canvas = canvas;
			this.elements = elements;
		}

		public virtual bool Snap(GripType type, ref Point delta) { return false; }

		public void Topmost()
		{
			if (selectedElement != null) 
			{
				Reorder(0);
			}
		}

		public void Bottommost()
		{
			if (selectedElement != null)
			{
				Reorder(elements.Count - 1);
			}
		}

		public void MoveUp()
		{
			if (selectedElement != null)
			{
				int idx = elements.IndexOf(selectedElement);

				if (idx > 0)
				{
					Reorder(idx - 1);
				}
			}
		}

		public void MoveDown()
		{
			if (selectedElement != null)
			{
				int idx = elements.IndexOf(selectedElement);

				if (idx < elements.Count - 1)
				{
					Reorder(idx + 1);
				}
			}
		}

		public void DeleteElement()
		{
			if (selectedElement != null)
			{
				selectedElement.DetachAll();
				EraseTopToBottom(elements);
				elements.Remove(selectedElement);
				selectedElement.Dispose();
				selectedElement = null;
				selectedAnchor = null;
				showingAnchorsElement = null;
				dragging = false;
				DrawBottomToTop(elements);
				ElementSelected.Fire(this, new ElementEventArgs());
				// Need to refresh the entire screen to remove the element from the screen itself.
				canvas.Invalidate();
			}
		}

		protected void Reorder(int n)
		{
			EraseTopToBottom(elements);
			elements.Swap(n, elements.IndexOf(selectedElement));
			DrawBottomToTop(elements);
			UpdateScreen(elements);
		}

		public void Redraw(GraphicElement el, int dx=0, int dy=0)
		{
			var els = EraseTopToBottom(el, dx, dy);
			DrawBottomToTop(els, dx, dy);
			UpdateScreen(els, dx, dy);
		}

		public void Redraw(GraphicElement el, Action<GraphicElement> afterErase)
		{
			var els = EraseTopToBottom(el);
			UpdateScreen(els);
			afterErase(el);
			DrawBottomToTop(els);
			UpdateScreen(els);
		}

		public void Insert(GraphicElement el)
		{
			elements.Insert(0, el);
			Redraw(el);
		}

		public void UpdateSize(GraphicElement el, ShapeAnchor anchor, Point delta)
		{
			Point adjustedDelta = anchor.AdjustedDelta(delta);
			Rectangle newRect = anchor.Resize(el.DisplayRectangle, adjustedDelta);
			UpdateDisplayRectangle(el, newRect, adjustedDelta);
			UpdateConnections(el);
		}

		/// <summary>
		/// Direct update of display rectangle, used in DynamicConnector.
		/// </summary>
		public void UpdateDisplayRectangle(GraphicElement el, Rectangle newRect, Point delta)
		{
			int dx = delta.X.Abs();
			int dy = delta.Y.Abs();
            var els = EraseTopToBottom(el, dx, dy);
			el.DisplayRectangle = newRect;
			el.UpdatePath();
			DrawBottomToTop(els, dx, dy);
			UpdateScreen(els, dx, dy);
		}

		protected void UpdateConnections(GraphicElement el)
		{
			el.Connections.ForEach(c =>
			{
				// Connection point on shape.
				ConnectionPoint cp = el.GetConnectionPoints().Single(cp2 => cp2.Type == c.ElementConnectionPoint.Type);
				c.ToElement.MoveAnchor(cp, c.ToConnectionPoint);
			});
		}

		public void MoveElement(GraphicElement el, Point delta)
		{
			if (el.OnScreen())
			{
                int dx = delta.X.Abs();
                int dy = delta.Y.Abs();
                var els = EraseTopToBottom(el, dx, dy);
				el.Move(delta);
				el.UpdatePath();
				DrawBottomToTop(els, dx, dy);
				UpdateScreen(els, dx, dy);
			}
			else
			{
				el.CancelBackground();
				el.Move(delta);
				// TODO: Display element if moved back on screen at this point?
			}
		}

        // "Smart" move, erases everything first, moves all elements, then redraws them.
        public void MoveAllElements(Point delta)
        {
            EraseTopToBottom(elements);

            elements.ForEach(e =>
            {
                e.Move(delta);
                e.UpdatePath();
            });

            int dx = delta.X.Abs();
            int dy = delta.Y.Abs();
            DrawBottomToTop(elements, dx, dy);
            UpdateScreen(elements, dx, dy);
        }

        public void SaveAsPng(string filename)
		{
			// Get boundaries of of all elements.
			int x1 = elements.Min(e => e.DisplayRectangle.X);
			int y1 = elements.Min(e => e.DisplayRectangle.Y);
			int x2 = elements.Max(e => e.DisplayRectangle.X + e.DisplayRectangle.Width);
			int y2 = elements.Max(e => e.DisplayRectangle.Y + e.DisplayRectangle.Height);
			int w = x2 - x1 + 10;
			int h = y2 - y1 + 10;
			Canvas pngCanvas = new Canvas();                                      
			pngCanvas.CreateBitmap(w, h);
			Graphics gr = pngCanvas.AntiAliasGraphics;

			gr.Clear(Color.White);
			Point offset = new Point(-(x1-5), -(y1-5));
			Point restore = new Point(x1-5, y1-5);

			elements.AsEnumerable().Reverse().ForEach(e =>
			{
				e.Move(offset);
				e.UpdatePath();
				e.SetCanvas(pngCanvas);
				e.Draw(gr);
				e.DrawText(gr);
				e.SetCanvas(canvas);
				e.Move(restore);
				e.UpdatePath();
			});

			pngCanvas.Bitmap.Save(filename, System.Drawing.Imaging.ImageFormat.Png);
			pngCanvas.Dispose();
		}

		/// <summary>
		/// Recursive loop to get all intersecting rectangles, including intersectors of the intersectees, so that all elements that
		/// are affected by an overlap redraw are erased and redrawn, otherwise we get artifacts of some intersecting elements when intersection count > 2.
		/// </summary>
		protected void FindAllIntersections(List<GraphicElement> intersections, GraphicElement el, int dx = 0, int dy = 0)
		{
			// Cool thing here is that if the element has no intersections, this list still returns that element because it intersects with itself!
			elements.Where(e => !intersections.Contains(e) && e.UpdateRectangle.IntersectsWith(el.UpdateRectangle.Grow(dx, dy))).ForEach((e) =>
			{
				intersections.Add(e);
				FindAllIntersections(intersections, e);
			});
		}

		protected IEnumerable<GraphicElement> EraseTopToBottom(GraphicElement el, int dx = 0, int dy = 0)
		{
			List<GraphicElement> intersections = new List<GraphicElement>();
			FindAllIntersections(intersections, el, dx, dy);
			IEnumerable<GraphicElement> els = intersections.OrderBy(e => elements.IndexOf(e));
			els.Where(e => e.OnScreen(dx, dy)).ForEach(e => e.Erase());

			return els;
		}

		protected void EraseTopToBottom(IEnumerable<GraphicElement> els)
		{
			els.Where(e => e.OnScreen()).ForEach(e => e.Erase());
		}

		protected void DrawBottomToTop(IEnumerable<GraphicElement> els, int dx = 0, int dy = 0)
		{
			els.Reverse().Where(e => e.OnScreen(dx, dy)).ForEach(e =>
			{
				e.GetBackground();
				e.Draw();
			});
		}

		protected void UpdateScreen(IEnumerable<GraphicElement> els, int dx = 0, int dy = 0)
		{
			// Is this faster than creating a unioned rectangle?  Dunno, because the unioned rectangle might include a lot of space not part of the shapes, like something in an "L" pattern.
			els.Where(e => e.OnScreen(dx, dy)).ForEach(e => e.UpdateScreen(dx, dy));
		}

		protected void CanvasPaintComplete(Canvas canvas)
		{
			DrawBottomToTop(elements);
		}
	}
}
