// Deliberately no Entity Framework or cloud SDKs here: those live in modules
// (modules/), so nothing in the app can come to depend on which database or
// storage is behind the interfaces. ArchitectureTests checks it.
global using WebBase.Data;
global using WebBase.Models;
global using WebBase.FileStorage;
