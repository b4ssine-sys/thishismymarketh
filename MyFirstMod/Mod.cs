using ICities;

namespace MyFirstMod
{
    // The single required entry point. The game discovers this class via
    // reflection when the assembly is loaded from the Mods folder.
    public class Mod : IUserMod
    {
        // Bump this on each Workshop update. Keep it in step with the
        // <Version> in MyFirstMod.csproj.
        public const string Version = "1.0.0";

        // Change the text before the version to whatever you want the
        // Workshop item titled.
        public string Name
        {
            get { return "Municipal Bond Market " + Version; }
        }

        public string Description
        {
            get
            {
                return "Adds a municipal bond market with credit ratings, " +
                       "coupon payments, and secondary trading. Built for Cities: Skylines 1.21.1-f9.";
            }
        }

        // To add an in-game options page later, implement:
        //
        // public void OnSettingsUI(UIHelperBase helper)
        // {
        //     UIHelperBase group = helper.AddGroup(Name);
        //     group.AddCheckbox("Enable feature", true, isChecked => { /* ... */ });
        // }
    }
}
