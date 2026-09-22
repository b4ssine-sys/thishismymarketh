// CI-only stub: compiled solely by Stubs/GameStubs.csproj, which defines
// GAME_STUBS. Inside the game these would collide with the real assemblies.
#if GAME_STUBS
namespace ICities
{
    public interface IUserMod
    {
        string Name { get; }
        string Description { get; }
    }

    public interface IEconomy
    {
    }

    public interface IBuilding
    {
    }

    public interface ISerializableData
    {
        void SaveData(string id, byte[] data);
        byte[] LoadData(string id);
    }

    public enum LoadMode
    {
        NewGame,
        LoadGame,
        NewMap,
        LoadMap,
        NewAsset,
        LoadAsset,
        NewScenario,
        LoadScenario,
        NewTheme,
        LoadTheme
    }

    public abstract class EconomyExtensionBase
    {
        public IEconomy economy;
        public virtual long OnUpdateMoneyAmount(long internalMoneyAmount) { return internalMoneyAmount; }
        public virtual void OnCreated(IEconomy economy) { }
        public virtual void OnReleased() { }
    }

    public abstract class LoadingExtensionBase
    {
        public virtual void OnLevelLoaded(LoadMode mode) { }
        public virtual void OnLevelUnloading() { }
        public virtual void OnCreated(object loading) { }
        public virtual void OnReleased() { }
    }

    public abstract class SerializableDataExtensionBase
    {
        public ISerializableData serializableDataManager;
        public virtual void OnSaveData() { }
        public virtual void OnLoadData() { }
        public virtual void OnCreated(ISerializableData serializedData) { }
        public virtual void OnReleased() { }
    }

    public abstract class BuildingExtensionBase
    {
        public virtual void OnCreated(IBuilding building) { }
        public virtual void OnReleased() { }
        public virtual void OnBuildingCreated(ushort id) { }
        public virtual void OnBuildingReleased(ushort id) { }
    }
}

#endif
