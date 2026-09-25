// CI-only stub: compiled solely by Stubs/GameStubs.csproj, which defines
// GAME_STUBS. Inside the game these would collide with the real assemblies.
#if GAME_STUBS
namespace ColossalFramework
{
    public static class Singleton<T> where T : class
    {
        public static T instance;
    }

    public struct Array16<T>
    {
        public T[] m_buffer;
        public uint m_size;
    }

    public struct Array32<T>
    {
        public T[] m_buffer;
        public uint m_size;
    }

    public class SimulationManager
    {
        public System.DateTime m_currentGameTime;
    }

    public class EconomyManager
    {
        public enum Resource
        {
            LoanAmount,
            LoanPayment,
            PublicIncome,
            Maintenance,
            PolicyCost
        }

        public long LastCashAmount;

        // virtual only so the engine test harness can fake the treasury.
        public virtual int FetchResource(Resource resource, int amount, ItemClass.Service service, ItemClass.SubService subService, ItemClass.Level level)
        {
            return amount;
        }

        public virtual int AddResource(Resource resource, int amount, ItemClass.Service service, ItemClass.SubService subService, ItemClass.Level level)
        {
            return amount;
        }
    }

    public class DistrictManager
    {
        public Array16<District> m_districts;
    }

    public struct District
    {
        public DistrictPopulationData m_populationData;
        public byte m_finalHappiness;
    }

    public struct DistrictPopulationData
    {
        public uint m_finalCount;
    }

    public class CitizenManager
    {
        public Array32<Citizen> m_citizens;
        public int m_citizenCount;
    }

    public struct Citizen
    {
        [System.Flags]
        public enum Flags : uint
        {
            None = 0,
            Created = 1
        }

        public enum Education
        {
            Uneducated = 0,
            OneSchool = 1,
            TwoSchools = 2,
            ThreeSchools = 3
        }

        public Flags m_flags;
        public byte m_health;
        public byte m_wellbeing;
        public Education EducationLevel;
        public ushort m_workBuilding;
    }

    public class BuildingManager
    {
        public Array16<Building> m_buildings;
    }

    public struct Building
    {
        [System.Flags]
        public enum Flags : uint
        {
            None = 0,
            Created = 1
        }

        public Flags m_flags;
        public BuildingInfo Info;
    }

    public class BuildingInfo
    {
        public ItemClass m_class;
    }

    public class ItemClass
    {
        public enum Service
        {
            None = 0,
            Residential = 1,
            Commercial = 2,
            Industrial = 3,
            Office = 4,
            Electricity = 5,
            Water = 6,
            Beautification = 7,
            Garbage = 8,
            HealthCare = 9,
            PoliceDepartment = 10,
            Education = 11,
            Monument = 12,
            FireDepartment = 13,
            PublicTransport = 14,
            Road = 20
        }

        public enum SubService
        {
            None = 0,
            ResidentialLow = 1,
            ResidentialHigh = 2,
            ResidentialLowEco = 3,
            ResidentialHighEco = 4
        }

        public enum Level
        {
            None = 0,
            Level1 = 1,
            Level2 = 2,
            Level3 = 3,
            Level4 = 4,
            Level5 = 5
        }

        public Service m_service;
        public SubService m_subService;
        public Level m_level;
    }
}

namespace ColossalFramework.UI
{
    public enum UIHorizontalAlignment { Left, Center, Right }
    public enum UIVerticalAlignment { Top, Middle, Bottom }

    public class UIComponent : UnityEngine.MonoBehaviour
    {
        public UnityEngine.Vector2 size;
        public UnityEngine.Vector3 relativePosition;
        public UnityEngine.Vector3 absolutePosition;
        public float width;
        public float height;
        public string tooltip;
        public bool isVisible;
        public bool clipChildren;
        public bool canFocus;
        public bool isInteractive;

        public T AddUIComponent<T>() where T : UIComponent, new() { return new T(); }
        public UIComponent AddUIComponent(System.Type type) { return null; }

        public UIView GetUIView() { return null; }

        public event MouseEventHandler eventMouseWheel;

        public virtual void Show() { isVisible = true; }
        public virtual void Hide() { isVisible = false; }
        public void BringToFront() { }
        public virtual void Start() { }
        public virtual void Update() { }
        public virtual void OnDestroy() { }
    }

    public delegate void MouseEventHandler(UIComponent component, UIMouseEventParameter eventParam);

    public class UIMouseEventParameter
    {
        public float wheelDelta;
        public void Use() { }
    }

    public class UIPanel : UIComponent
    {
        public string backgroundSprite;
        public UnityEngine.Color32 color;
        public UIComponent autoLayout;
    }

    public class UILabel : UIComponent
    {
        public string text;
        public float textScale;
        public UnityEngine.Color32 textColor;
        public bool wordWrap;
        public bool autoSize;
        public UnityEngine.Vector2 padding;
        public string prefix;
        public string suffix;
        public UIHorizontalAlignment textAlignment;
        public UIVerticalAlignment verticalAlignment;
    }

    public class UIButton : UIComponent
    {
        public string text;
        public float textScale;
        public UnityEngine.Color32 textColor;
        public UnityEngine.Color32 hoveredTextColor;
        public UnityEngine.Color32 focusedTextColor;
        public UnityEngine.Color32 disabledTextColor;
        public string normalBgSprite;
        public string hoveredBgSprite;
        public string pressedBgSprite;
        public string focusedBgSprite;
        public string disabledBgSprite;
        public bool isEnabled;

        public event MouseEventHandler eventClick;

        public void SimulateClick() { if (eventClick != null) eventClick(this, null); }
    }

    public class UIView : UIComponent
    {
        public float fixedWidth;
        public float fixedHeight;
        public static UIView GetAView() { return new UIView(); }
    }

    public class UIDragHandle : UIComponent
    {
        public UIComponent target;
    }
}

#endif
