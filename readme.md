# How to discover missing type-maps for mapping enum to enum in AutoMapper?

>Ok folks, this is a rather long question, where I´m trying my best to describe the current situation and provide some meaningful context, before I´m comming to my actual question.

## TL;DR;
I need a way to identify missing type-maps of mappings from enum-to-enum-properties in AutoMapper, so that I can configure to use mapping **per-name** instead of AutoMapper´s default behavior of mapping **per-value** (which is, using the internal integer-representation of an enum).

## Some Context
So my team and I are maintaining this rather complex set of REST-APIs...complex at least when it comes down to the actual object-graphs involved.
We have to deal with some hundreds of models in total. 
To raise structural complexity, the original architectures went with full n-tier-style on an inner API-level.

On top of that, we´re having multiple of such architectured services, which sometimes need calling each other.
This is achieved through either ordinary http-calls here, some messaging there, you get the idea. 

For letting an API communicate with another, and to maintain SOA- and/or microservice-principles, every API at least provides a corresponding client-library, which manages communication with it´s representing API, regardless of the actual underlying protocol involved.

Boiling this down, this incorporates at least the following layers per API (top-down)

- Client-Layer
- API-Layer
- Domain-Layer
- Persistence-Layer 

Additionally, all those layers maintain their own representation of the various models. Often, those are 1:1 representation, just in another namespace. Sometimes there are more significant differences in between these layers. It depends...

To reduce boiler-plate when communicating between these layers, we´re falling back on [AutoMapper] most of the time (hate it or love it).

## The problem:
As we evolve our overall system, we more and more noticed problems when mapping enum-to-enum properties within the various representations of the models.
Sometimes it´s because some dev just forgot to add a new enum-value in one of the layers, sometimes we re-generated an Open-API based generated client, etc., which then leads to out-of-sync definitions of those enums. 
Another issue happens, if, for some reason, these enums are par member-wise, but have another ordering or naming, hence internal representation of the actual enum value. This can get really nasty, especially when we remember how AutoMapper handles enum-to-enum-mappings per default: per Value.

Let´s say we have this (very very over-simplified) model-representations

```C#

public enum Source { A, B, C, D }

public enum Destination { A, C, B }

class SourceType
{
    public string Name { get; set; }
    public Source Enum { get; set; }
}

class DestinationType
{
    public string Name { get; set; }
    public Destination Enum { get; set; }
}

```


Now let´s say our AutoMapper config looks something like this:

```C#

var mapperConfig = new MapperConfiguration(config =>
{
    config.CreateMap<SourceType, DestinationType>();
});


```

So mapping the following models is kind of a jeopardy, semantic-wise (or at least offers some very odd fun while debugging). 


```C#
var a = mapper.Map<DestinationType>(new SourceType {
    Name = "A", Enum = Source.A
); 
//a is { 
//  Name = "A", Enum = "A" <-- ✔️ looks good
//}

var b = mapper.Map<DestinationType>(new SourceType {
    Name = "B", Enum = Source.B
); 
//b is { 
//  Name = "B", Enum = "C" <-- ❗ possible semantic inconsistency
//}

var c = mapper.Map<DestinationType>(new SourceType {
    Name = "C", Enum = Source.C
); 
//c is { 
//  Name = "C", Enum = "B" <-- ❗ possible semantic inconsistency
//}

var d = mapper.Map<DestinationType>(new SourceType {
    Name = "C", Enum = Source.D
); 
//d is { 
//  Name = "C", Enum = "3" <-- ❗ wtf, are you serious?
//}

```

Fewer fun can be observed, when this ultimately causes some nasty production-bugs, which also may have more or less serious business-impact - especially when this kind of issue only happens during run-time, rather than test- and/or build-time.


Additionally, the problem is not exclusive to n-tier-ish architectures, but could also be an issue in orthogonal/onion-/clean-ish-architecture styles (wheras in such cases it should be more likely that such value-types would be placed somewhere in the center of the APIs, rather than on every corner / outer-ring /adapter-layer or whatever the current terminology is)

## A (temporary) solution
Despite trying to reduce the shear amount of redundancy within the respective layers, or (manually) maintaining explicit enum-values within the definitions itself (which both are valid options, but heck, this is a lot of PITA-work), there is not much left to do while trying to mitigate this kind of issues.

Gladly, there is a nice option available, which levereages mapping enum-to-enum-properties **per-name** instead of **per-value**, as well as doing more customization on a very fine-granular level on a per-member-basis.

### [AutoMapper.Extensions.EnumMapping] to the rescue!

from the docs:
> The package AutoMapper.Extensions.EnumMapping will map all values from Source type to Destination type if both enum types have the same value (or by name or by value)

and
> This package adds an extra EnumMapperConfigurationExpressionExtensions.EnableEnumMappingValidation extension method to extend the existing AssertConfigurationIsValid() method to validate also the enum mappings.


To enable and cusomize mappings, one should just need to create the respective type-maps within AutoMapper-configuration:

```C#
var mapperConfig = new MapperConfiguration(config =>
{
    config.CreateMap<SourceType, DestinationType>();
    config.CreateMap<Source, Destination>().ConvertUsingEnumMapping(opt => opt.MapByName());
    config.EnableEnumMappingValidation();
});

mapperConfig.AssertConfigurationIsValid();

```

Which then would validate even enum-to-enum mappings.

## The question (finally ^^)

As our team previously did not (need to) configure AutoMapper with maps for every enum-to-enum mapping (as was the case for dynamic-maps in previous-versions of AutoMapper), we´re a bit lost on how to efficiently and deterministically discover every map needed to be configured this way. Especially, as we´re dealing with possibly a couple of dozens of such cases per api (and per layer).

How could we possibly get to the point, where we have validated and adapted our existing code-base, as well as further preventing this kind of dumbery in the first place


[AutoMapper.Extensions.EnumMapping]:(https://docs.automapper.org/en/stable/Enum-Mapping.html)





# Leverage custom validation to discover missing mappings during test-time

Ok, now this approach leverages a multi-phased analysis, best fitted into an unit-test (which may already be present in your solution(s), nevertheless).
It´s not a golden gun to magically solve all your issues (possibly) prevalent, but puts you into a very tight dev-loop



The steps involved are
1. enable Validation of your AutoMapper-Configuration
2. use AutoMapper custom-validation to discover missing type maps
3. add and configure missing type-maps
4. ensure maps are valid
5. adapt changes in enums, or mapping logic (whatever best fits)
    > this can be cumbersome and needs extra attention, depending on the issues dsicovered by this approach
6. rince and repeat
> Examples below use xUnit. Use whatever you might have at hands.


## 0. starting point
We´re starting with your initial AutoMapper-configuration:
```C#
var mapperConfig = new MapperConfiguration(config =>
{
    config.CreateMap<SourceType, DestinationType>();
});

```

## 1. enable Validation of your AutoMapper-Configuration
Somewhere within your test-suit, ensure you are validating your AutoMapper configuration:
```C#

[Fact]
public void MapperConfigurationIsValid() => mapperConfig.AssertConfigurationIsValid();

```

## 2. use AutoMapper custom-validation to discover missing type maps
Now modify your AutoMapper-configuration to this:
```C#
mapperConfig = new MapperConfiguration(config =>
            {
                config.CreateMap<SourceType, DestinationType>();

                config.Advanced.Validator(context => {

                    if (!context.Types.DestinationType.IsEnum) return;
                    if (!context.Types.SourceType.IsEnum) return;
                    if (context.TypeMap is not null) return;

                    var message = $"config.CreateMap<{context.Types.SourceType}, {context.Types.DestinationType}>().ConvertUsingEnumMapping(opt => opt.MapByName());";

                    throw new AutoMapperConfigurationException(message);
                });

                config.EnableEnumMappingValidation();
            });
```


this does a couple of things:
1. look for mappings, that map **from** an enum **to** an enum
2. which have no type-map associated to them (that is, where "generated" by automapper itself and hence are lacking an explicit ```CreateMap``` call)
```C#
    if (!context.Types.DestinationType.IsEnum) return;
    if (!context.Types.SourceType.IsEnum) return;
    if (context.TypeMap is not null) return;
```


3. raise an error, which message is the equivalent of the actual call missing to ```CreateMap```
```C#
var message = $"config.CreateMap<{context.Types.SourceType}, {context.Types.DestinationType}>().ConvertUsingEnumMapping(opt => opt.MapByName());";

throw new AutoMapperConfigurationException(message);
```

## 3. add and configure missing type-maps

re-running our previous test, which should fail now, now should output something like this:

```
AutoMapper.AutoMapperConfigurationException : config.CreateMap<Sample.AutoMapper.EnumValidation.Source, Sample.AutoMapper.EnumValidation.Destination>().ConvertUsingEnumMapping(opt => opt.MapByName());
```

And boom, there you go. The missing type-map configuration call on a silver-plate.

Now copy that line and place it somwhere suitable withing your AutoMapper-configuration.

For this post I´m just putting it below the existing one:

```C#

config.CreateMap<SourceType, DestinationType>();
config.CreateMap<{context.Types.SourceType}, {context.Types.DestinationType}>().ConvertUsingEnumMapping(opt => opt.MapByName());

```

> in a real-world scenario, this would be a line for every enum-to-enum mapping that not already has a type-map associated to it within the AutoMapper-configuration. Depending on how you actually configure AutoMapper, this line could need to be slightly adopted to your needs, e.g. for usage in MappingProfiles.

5. adapt changes in enums

Re-run the test from above, which should fail now, too, as there are incompatible enum-values.
The output should look something like this:

```
AutoMapper.AutoMapperConfigurationException : Missing enum mapping from Sample.AutoMapper.EnumValidation.Source to Sample.AutoMapper.EnumValidation.Destination based on Name
    The following source values are not mapped:
     - D
```

There you go, AutoMapper discovered a missing enum-value.
What´s to do now heavily depends on your solution and cannot be covered in a SO-post. So take actions to mitigate, then.

## 6. rince and repeat
Go back to 3. until all issues are solved.

From then on, you should have a saftey-net in place, that should prevent you from falling into that kind of trap in the future.

>However, note that mapping **per-name** instead of **per-value** *might* have a negative impact, performance-wise. That should definetley be taken into account when applying this kind of change to your code-base. But with all those inter-layer-mappings present I would guess a possible bottleneck is in another castle, Mario ;)








