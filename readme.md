# How to discover missing type-maps for enum-to-enum mappings in AutoMapper?

>Ok folks, this is a rather long question, where I´m trying my best to describe the current situation and provide some meaningful context, before I´m comming to my actual question.

## TL;DR;
I need a way to identify invalid enum-to-enum mappings, which might cause runtime-issues, as their definitions diverged over the time.


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

To reduce boiler-plate when communicating between these layers, we´re falling back on [AutoMapper](1) most of the time (hate it or love it).

## The problem:
As we evolve our overall system, we more and more noticed problems when mapping enum-to-enum properties within the various representations of the models.
Sometimes it´s because some dev just forgot to add a new enum-value in one of the layers, sometimes we re-generated an Open-API based generated client, etc., which then leads to out-of-sync definitions of those enums. The primary issue is, that a source enum may have more values then the target enum.
Another issue might occur, when there are slight differences in the naming, e.g. Executer vs. Executor


Let´s say we have this (very very over-simplified) model-representations

```C#

    public enum Source { A, B, C, D, Executer, A1, B2, C3 } // more values than below

    public enum Destination { C, B, X, Y, A, Executor }  //fewer values, different ordering, no D, but X, Y and a Typo


    class SourceType
    {
        public Source[] Enums { get; set; }
    }

    class DestinationType
    {
        public Destination[] Enums { get; set; }
    }

```


Now let´s say our AutoMapper config looks something like this:

```C#

var problematicMapper = new MapperConfiguration(config =>
{
    config.CreateMap<SourceType, DestinationType>();
}).CreateMapper();


```

So mapping the following model is kind of a jeopardy, semantic-wise (or at least offers some very odd fun while debugging). 


```C#
    var destination = problematicMapper.Map<DestinationType>(new SourceType()
    {
        Enums = new []
        {
            Source.A,
            Source.B,
            Source.C,
            Source.D,
            Source.Executer,
            Source.A1,
            Source.B2,
            Source.C3
        }
    });

    var mappedValues = destination.Enums.Select(x => x.ToString()).ToArray();
    
    testOutput.WriteLine(string.Join(Environment.NewLine, mappedValues));
    /*  
        Source.A            => A           <- ✔️ ok
        Source.B            => b           <- ✔️ok
        Source.C            => c           <- ✔️ok
        Source.D            => Y           <- 🤷‍♀️ whoops
        Source.Executer     => A           <- 🧏‍♂️ wait, what?
        Source.A1           => Executor    <- 🙊 nah
        Source.B2           => 6           <- 🙉 wtf?
        Source.C3           => 7           <- 🙈 wth?
        */

```

> bare with me, as some situations here are staged and possibly more extreme than found in reality. Just wanted to point out some weird behavior, even with AutoMapper trying to gracefully handle most cases, like the re-orderings or different casings. Currently, we are facing either more values in the source-enum, or slightly differences in naming / typos

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

How could we possibly get to the point, where we have validated and adapted our existing code-base, as well as further preventing this kind of dumbery in the first place?


[1]:(https://docs.automapper.org/en/stable/Enum-Mapping.html)





# Leverage custom validation to discover missing mappings during test-time

Ok, now this approach leverages a multi-phased analysis, best fitted into an unit-test (which may already be present in your solution(s), nevertheless).
It´s not a golden gun to magically solve all your issues which may be prevalent, but puts you into a very tight dev-loop which should help clean up things.
Period.



The steps involved are
1. enable validation of your AutoMapper-configuration
2. use AutoMapper custom-validation to discover missing type maps
3. add and configure missing type-maps
4. ensure maps are valid
5. adapt changes in enums, or mapping logic (whatever best fits)
    > this can be cumbersome and needs extra attention, depending on the issues discovered by this approach
6. rinse and repeat
> Examples below use xUnit. Use whatever you might have at hands.


## 0. starting point
We´re starting with your initial AutoMapper-configuration:
```C#
var mapperConfig = new MapperConfiguration(config =>
{
    config.CreateMap<SourceType, DestinationType>();
});

```

## 1. enable validation of your AutoMapper-Configuration
Somewhere within your test-suit, ensure you are validating your AutoMapper-configuration:
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


This does a couple of things:
1. look for mappings, that map **from** an enum **to** an enum
2. which have no type-map associated to them (that is, they were "generated" by AutoMapper itself and hence are lacking an explicit ```CreateMap``` call)
```C#
    if (!context.Types.DestinationType.IsEnum) return;
    if (!context.Types.SourceType.IsEnum) return;
    if (context.TypeMap is not null) return;
```


3. Raise an error, which message is the equivalent of the actual call missing to ```CreateMap```
```C#
var message = $"config.CreateMap<{context.Types.SourceType}, {context.Types.DestinationType}>().ConvertUsingEnumMapping(opt => opt.MapByName());";

throw new AutoMapperConfigurationException(message);
```

## 3. add and configure missing type-maps

Re-running our previous test, which should fail now, should output something like this:

```
AutoMapper.AutoMapperConfigurationException : config.CreateMap<Sample.AutoMapper.EnumValidation.Source, Sample.AutoMapper.EnumValidation.Destination>().ConvertUsingEnumMapping(opt => opt.MapByName());
```

And boom, there you go. The missing type-map configuration call on a silver-plate.

Now copy that line and place it somewhere suitable withing your AutoMapper-configuration.

For this post I´m just putting it below the existing one:

```C#

config.CreateMap<SourceType, DestinationType>();
config.CreateMap<Sample.AutoMapper.EnumValidation.Source, Sample.AutoMapper.EnumValidation.Destination>().ConvertUsingEnumMapping(opt => opt.MapByName());

```

> in a real-world scenario, this would be a line for every enum-to-enum mapping that not already has a type-map associated to it within the AutoMapper-configuration. Depending on how you actually configure AutoMapper, this line could need to be slightly adopted to your needs, e.g. for usage in MappingProfiles.

5. adapt changes in enums

Re-run the test from above, which should fail now, too, as there are incompatible enum-values.
The output should look something like this:

```
    AutoMapper.AutoMapperConfigurationException : Missing enum mapping from Sample.AutoMapper.EnumValidation.Source to Sample.AutoMapper.EnumValidation.Destination based on Name
    The following source values are not mapped:
     - B
     - C
     - D
     - Executer
     - A1
     - B2
     - C3
```

There you go, AutoMapper discovered missing or un-mappable enum-values.
> note that we lost automatic handling of differences in casing.

What´s to do now heavily depends on your solution and cannot be covered in a SO-post. So take appropriate actions to mitigate.

## 6. rinse and repeat
Go back to 3. until all issues are solved.


From then on, you should have a saftey-net in place, that should prevent you from falling into that kind of trap in the future.

>However, note that mapping **per-name** instead of **per-value** *might* have a negative impact, performance-wise. That should definetley be taken into account when applying this kind of change to your code-base. But with all those inter-layer-mappings present I would guess a possible bottleneck is in another castle, Mario ;)

A full wrapup of the samples shown in this post can be found in this [github-repo][1]


  [1]: https://github.com/earloc/Sample.AutoMapper.EnumValidation