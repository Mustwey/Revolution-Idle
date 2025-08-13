namespace Mod.Features
{
    public interface IFeature
    {
        string Name { get; }
        string Description { get; }
        int Order { get; }
        bool Enabled { get; set; }
        void Enable();
        void Disable();
        void Update();
    }
}