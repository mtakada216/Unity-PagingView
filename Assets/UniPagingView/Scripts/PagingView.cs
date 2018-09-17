using System;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace UnityEngine.UI.Extensions
{
    [SelectionBase]
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("UI/Extensions/PagingView")]
    public class PagingView : UIBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IEndDragHandler, IDragHandler, IScrollHandler, ICanvasElement, ILayoutElement, ILayoutGroup
    {
        [Serializable] public class PagingEvent : UnityEvent<int> {}

        [SerializeField] private RectTransform m_Content;
        public RectTransform Content
        {
            get { return m_Content; }
            set { m_Content = value; }
        }

        [SerializeField] private RectTransform[] m_Contents;
        public RectTransform[] Contents
        {
            get { return m_Contents; }
            set { m_Contents = value; }
        }

        [SerializeField] private bool m_Horizontal = true;
        public bool Horizontal
        {
            get { return m_Horizontal; }
            set { m_Horizontal = value; }
        }

        [SerializeField] private bool m_Vertical = true;
        public bool Vertical
        {
            get { return m_Vertical; }
            set { m_Vertical = value; }
        }

        [SerializeField] private float m_Elasticity = 0.1f;
        public float Elasticity
        {
            get { return m_Elasticity; }
            set { m_Elasticity = value; }
        }

        [SerializeField] private bool m_Inertia = true;
        public bool Inertia
        {
            get { return m_Inertia; }
            set { m_Inertia = value; }
        }

        [SerializeField] private float m_DecelerationRate = 0.135f;
        public float DecelerationRate
        {
            get { return m_DecelerationRate; }
            set { m_DecelerationRate = value; }
        }

        [SerializeField] private float m_ScrollSensitivity = 1.0f;
        public float ScrollSensitivity
        {
            get { return m_ScrollSensitivity; }
            set { m_ScrollSensitivity = value; }
        }

        [SerializeField] private RectTransform m_Viewport;
        public RectTransform viewport
        {
            get {return m_Viewport; }
            set { m_Viewport = value; SetDirtyCaching(); }
        }

        [SerializeField] private PagingEvent m_OnValueChanged = new PagingEvent();
        public PagingEvent OnValueChanged
        {
            get { return m_OnValueChanged; }
            set { m_OnValueChanged = value; }
        }

        private Vector2 m_PointerStartLocalCursor = Vector2.zero;
        private Vector2 m_ContentStartPosition = Vector2.zero;

        private RectTransform m_ViewRect;
        public RectTransform ViewRect
        {
            get
            {
                if (m_ViewRect == null)
                    m_ViewRect = m_Viewport;
                if (m_ViewRect == null)
                    m_ViewRect = (RectTransform) transform;
                return m_ViewRect;
            }
        }

        [NonSerialized] private RectTransform m_Rect;
        private RectTransform RectTransform
        {
            get
            {
                if (m_Rect == null)
                    m_Rect = GetComponent<RectTransform>();
                return m_Rect;
            }
        }

        private Vector2 m_Velocity;
        public Vector2 Velocity
        {
            get { return m_Velocity; }
            set { m_Velocity = value; }
        }

        private bool m_Dragging;

        private Bounds m_ViewBounds;
        private Bounds m_ContentBounds;
        private Bounds m_CurrentContentBounds;
        private Bounds m_PrevViewBounds;
        private Bounds m_PrevContentBounds;
        private Bounds m_PrevCurrentContentBounds;
        private Vector2 m_PrevPosition = Vector2.zero;

        private DrivenRectTransformTracker m_Tracker;

        [NonSerialized] private bool m_HasRebuiltLayout;

        private const float EPSILON = float.Epsilon;

        protected PagingView()
        {
            flexibleWidth = -1;
        }

        public void Rebuild(CanvasUpdate executing)
        {
            if (executing == CanvasUpdate.PostLayout)
            {
                UpdateBounds();
                UpdatePrevData();

                m_HasRebuiltLayout = true;
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
            SetDirty();
        }

        public virtual void LayoutComplete() {}

        public virtual void GraphicUpdateComplete() {}

        public virtual void CalculateLayoutInputHorizontal() {}

        public virtual void CalculateLayoutInputVertical() {}

        public virtual float minWidth { get { return -1; } }

        public virtual float preferredWidth { get { return -1; } }

        public virtual float flexibleWidth { get; private set; }

        public virtual float minHeight { get { return -1; } }

        public virtual float preferredHeight { get { return -1; } }

        public virtual float flexibleHeight { get { return -1; } }

        public virtual int layoutPriority { get { return -1; } }

        public virtual void SetLayoutHorizontal() { m_Tracker.Clear(); }

        public virtual void SetLayoutVertical()
        {
            m_ViewBounds = new Bounds(ViewRect.rect.center, ViewRect.rect.size);
            m_ContentBounds = GetBounds(m_Content);
            m_CurrentContentBounds = GetBounds(m_Contents[m_ContentIndex]);
        }

        public virtual void OnScroll(PointerEventData eventData)
        {
            if (!IsActive())
                return;

            EnsureLayoutHasRebuilt();
            UpdateBounds();

            Vector2 delta = eventData.scrollDelta;
            delta.y *= -1;
            if (Vertical && !Horizontal)
            {
                if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                    delta.y = delta.x;
                delta.x = 0;
            }

            if (Horizontal && !Vertical)
            {
                if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
                    delta.x = delta.y;
                delta.y = 0;
            }

            Vector2 position = m_Content.anchoredPosition;
            position += delta * m_ScrollSensitivity;

            SetContentAnchoredPosition(position);
            UpdateBounds();
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            m_Velocity = Vector2.zero;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (!IsActive())
                return;

            UpdateBounds();

            m_PointerStartLocalCursor = Vector2.zero;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(ViewRect, eventData.position,
                eventData.pressEventCamera, out m_PointerStartLocalCursor);
            m_ContentStartPosition = m_Content.anchoredPosition;
            m_Dragging = true;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            m_Dragging = false;

            JudgementIndex();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (!IsActive())
                return;

            Vector2 localCursor;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(ViewRect, eventData.position,
                eventData.pressEventCamera, out localCursor))
                return;

            UpdateBounds();

            var pointerDelta = localCursor - m_PointerStartLocalCursor;
            Vector2 position = m_ContentStartPosition + pointerDelta;
            Vector2 offset = CalculateOffset(m_ContentBounds, position - m_Content.anchoredPosition);
            position += offset;
            if (Math.Abs(offset.x) > EPSILON)
                position.x = position.x - RubberDelta(offset.x, m_ViewBounds.size.x);
            if (Math.Abs(offset.y) > EPSILON)
                position.y = position.y - RubberDelta(offset.y, m_ViewBounds.size.y);

            SetContentAnchoredPosition(position);
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            CanvasUpdateRegistry.RegisterCanvasElementForLayoutRebuild(this);
        }

        protected override void OnDisable()
        {
            CanvasUpdateRegistry.UnRegisterCanvasElementForRebuild(this);

            m_Tracker.Clear();
            m_Velocity = Vector2.zero;
            LayoutRebuilder.MarkLayoutForRebuild(RectTransform);

            base.OnDisable();
        }

        public override bool IsActive()
        {
            return base.IsActive() && m_Content != null;
        }

        private int m_PrevContentIndex;
        private int m_ContentIndex;
        private void LateUpdate()
        {
            if (!m_Content)
                return;

            EnsureLayoutHasRebuilt();
            UpdateBounds();
            float deltaTime = Time.unscaledDeltaTime;
            Vector2 offset = CalculateOffset(m_CurrentContentBounds, Vector2.zero);
            if (!m_Dragging && (offset != Vector2.zero || m_Velocity != Vector2.zero))
            {
                Vector2 position = m_Content.anchoredPosition;
                for (int axis = 0; axis < 2; ++axis)
                {
                    if (Math.Abs(offset[axis]) > EPSILON)
                    {
                        float speed = m_Velocity[axis];
                        position[axis] = Mathf.SmoothDamp(m_Content.anchoredPosition[axis], m_Content.anchoredPosition[axis] + offset[axis], ref speed, m_Elasticity, Mathf.Infinity, deltaTime);
                        m_Velocity[axis] = speed;
                    }
                    else if (m_Inertia)
                    {
                        m_Velocity[axis] *= Mathf.Pow(m_DecelerationRate, deltaTime);
                        if (Mathf.Abs(m_Velocity[axis]) < 1)
                            m_Velocity[axis] = 0;
                        position[axis] += m_Velocity[axis] * deltaTime;
                    }
                    else
                    {
                        m_Velocity[axis] = 0;
                    }
                }

                if (m_Velocity != Vector2.zero)
                    SetContentAnchoredPosition(position);
            }

            if (m_Dragging && m_Inertia)
            {
                Vector3 newVelocity = (m_Content.anchoredPosition - m_PrevPosition) / deltaTime;
                m_Velocity = Vector3.Lerp(m_Velocity, newVelocity, deltaTime * 10);
            }

            if (m_Dragging && m_Velocity != Vector2.zero)
                JudgementIndex(offset);

            if (!m_Dragging && m_PrevContentIndex != m_ContentIndex)
            {
                m_OnValueChanged.Invoke(m_ContentIndex);
                m_PrevContentIndex = m_ContentIndex;
            }

            if (m_ViewBounds != m_PrevViewBounds || m_ContentBounds != m_PrevContentBounds
                || m_ContentBounds != m_PrevCurrentContentBounds || m_Content.anchoredPosition != m_PrevPosition)
                UpdatePrevData();
        }

        public virtual void StopMovement()
        {
            m_Velocity = Vector2.zero;
        }

        protected virtual void SetContentAnchoredPosition(Vector2 position)
        {
            if (!m_Horizontal)
                position.x = m_Content.anchoredPosition.x;
            if (!m_Vertical)
                position.y = m_Content.anchoredPosition.y;

            if (position != m_Content.anchoredPosition)
            {
                m_Content.anchoredPosition = position;
                UpdateBounds();
            }
        }

        private void EnsureLayoutHasRebuilt()
        {
            if (m_HasRebuiltLayout && !CanvasUpdateRegistry.IsRebuildingLayout())
                Canvas.ForceUpdateCanvases();
        }

        private void UpdatePrevData()
        {
            if (m_Content == null)
                m_PrevPosition = Vector2.zero;
            else
                m_PrevPosition = m_Content.anchoredPosition;
            m_PrevViewBounds = m_ViewBounds;
            m_PrevContentBounds = m_ContentBounds;
            m_PrevCurrentContentBounds = m_ContentBounds;
        }

        private static float RubberDelta(float overStretching, float viewSize)
        {
            return (1 - (1 / ((Mathf.Abs(overStretching) * 0.55f / viewSize) + 1)))
                   * viewSize * Mathf.Sign(overStretching);
        }

        private void UpdateBounds()
        {
            m_ViewBounds = new Bounds(ViewRect.rect.center, ViewRect.rect.size);
            m_ContentBounds = GetBounds(m_Content);
            m_CurrentContentBounds = GetBounds(m_Contents[m_ContentIndex]);

            if (m_Content == null)
                return;

            Vector3 contentSize = m_ContentBounds.size;
            Vector3 contentPos = m_ContentBounds.center;
            Vector3 excess = m_ViewBounds.size - contentSize;
            if (excess.x > 0)
            {
                contentPos.x -= excess.x * (m_Content.pivot.x - 0.5f);
                contentSize.x = m_ViewBounds.size.x;
            }
            if (excess.y > 0)
            {
                contentPos.y -= excess.y * (m_Content.pivot.y - 0.5f);
                contentSize.y = m_ViewBounds.size.y;
            }

            m_ContentBounds.size = contentSize;
            m_ContentBounds.center = contentPos;
        }

        private readonly Vector3[] m_Corners = new Vector3[4];
        private Bounds GetBounds(RectTransform content)
        {
            if (m_Content == null)
                return new Bounds();

            var vMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var vMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            var toLocal = ViewRect.worldToLocalMatrix;
            content.GetWorldCorners(m_Corners);
            for (int j = 0; j < 4; j++)
            {
                Vector3 v = toLocal.MultiplyPoint3x4(m_Corners[j]);
                vMin = Vector3.Min(v, vMin);
                vMax = Vector3.Max(v, vMax);
            }

            var bounds = new Bounds(vMin, Vector3.zero);
            bounds.Encapsulate(vMax);
            return bounds;
        }

        private Vector2 CalculateOffset(Bounds bounds, Vector2 delta)
        {
            Vector2 offset = Vector2.zero;

            Vector2 min = bounds.min;
            Vector2 max = bounds.max;

            if (m_Horizontal)
            {
                min.x += delta.x;
                max.x += delta.x;
                if (min.x > m_ViewBounds.min.x)
                    offset.x = m_ViewBounds.min.x - min.x;
                else if (max.x < m_ViewBounds.max.x)
                    offset.x = m_ViewBounds.max.x - max.x;
            }

            if (m_Vertical)
            {
                min.y += delta.y;
                max.y += delta.y;
                if (max.y < m_ViewBounds.max.y)
                    offset.y = m_ViewBounds.max.y - max.y;
                else if (min.y > m_ViewBounds.min.y)
                    offset.y = m_ViewBounds.min.y - min.y;
            }

            return offset;
        }

        private void JudgementIndex()
        {
            if (Horizontal)
            {
                if (-m_Velocity.x > m_ViewBounds.size.x)
                    m_ContentIndex = Mathf.Clamp(m_PrevContentIndex + 1, 0, m_Contents.Length - 1);
                else if (m_Velocity.x > m_ViewBounds.size.x)
                    m_ContentIndex = Mathf.Clamp(m_PrevContentIndex - 1, 0, m_Contents.Length - 1);
            }

            if (Vertical)
            {
                if (m_Velocity.y > m_ViewBounds.size.y)
                    m_ContentIndex = Mathf.Clamp(m_PrevContentIndex + 1, 0, m_Contents.Length - 1);
                else if (-m_Velocity.y > m_ViewBounds.size.y)
                    m_ContentIndex = Mathf.Clamp(m_PrevContentIndex - 1, 0, m_Contents.Length - 1);
            }
        }

        private void JudgementIndex(Vector2 offset)
        {
            if (Horizontal)
            {
                if (offset.x > m_ViewBounds.extents.x)
                    m_ContentIndex = Mathf.Clamp(m_ContentIndex + 1, 0, m_Contents.Length - 1);
                else if (-offset.x > m_ViewBounds.extents.x)
                    m_ContentIndex = Mathf.Clamp(m_ContentIndex - 1, 0, m_Contents.Length - 1);
            }

            if (Vertical)
            {
                if (-offset.y > m_ViewBounds.extents.y)
                    m_ContentIndex = Mathf.Clamp(m_ContentIndex + 1, 0, m_Contents.Length - 1);
                else if (offset.y > m_ViewBounds.extents.y)
                    m_ContentIndex = Mathf.Clamp(m_ContentIndex - 1, 0, m_Contents.Length - 1);
            }
        }

        protected void SetDirty()
        {
            if (!IsActive())
                return;
            LayoutRebuilder.MarkLayoutForRebuild(RectTransform);
        }

        protected void SetDirtyCaching()
        {
            if (!IsActive())
                return;

            CanvasUpdateRegistry.RegisterCanvasElementForLayoutRebuild(this);
            LayoutRebuilder.MarkLayoutForRebuild(RectTransform);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            SetDirtyCaching();
        }
#endif
    }
}
