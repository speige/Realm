using Realm.Simulation.Tools;
using System.Diagnostics;
using System.Xml;
using Templates;
using Templates.Definitions;
using Templates.Ids;

/*
TemplateSchemaGenerator.GenerateSchema(@"c:\temp\schema.json");
TemplateLoader.LoadTemplatesFromJson(@"c:\temp\templates.json");
var a = ((Templates.Ids.UniqueId<Templates.Definitions.Presentation2dTemplate>)"plate_armor_name").Get().DisplayName;
var b = (UniqueId<Templates.Definitions.Presentation2dTemplate>)"plate_armor_name";

UniqueId<Presentation2dTemplate> c = "plate_armor_name";
var d = c.Get();
var e = "plate_armor_name".GetTemplate<Presentation2dTemplate>();
var old = "plate_armor_name".RegisterTemplate<Presentation2dTemplate>(x => x with { DisplayName = "fart" });
TemplateRegistry.Register<Presentation2dTemplate>("plate_armor_name").Update(x => x with { DisplayName = "asdf" });
TemplateRegistry.Register<Presentation2dTemplate>("plate_armor_name", x => x with { DisplayName = "qwer" });

var f = TemplateRegistry.Register<Presentation2dTemplate>("plate_armor_name").DisplayName;

//var test = old with { UniqueId = 98198 };
TemplateRegistry.UpdateRegistration(old with { DisplayName = "hello" });
TemplateRegistry.UpdateRegistration(old with { UniqueId = 5 });
var latest = "plate_armor_name".GetTemplate<Presentation2dTemplate>();

"human_footman".GetTemplate<UnitTemplate>().ArmorType.Get().Presentation2d.Get().Update(x => x with { DisplayName = "test" });
*/

Debugger.Break();
