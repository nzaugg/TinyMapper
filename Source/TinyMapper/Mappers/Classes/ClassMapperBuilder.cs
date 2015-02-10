using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Nelibur.ObjectMapper.CodeGenerators;
using Nelibur.ObjectMapper.CodeGenerators.Emitters;
using Nelibur.ObjectMapper.Core.DataStructures;
using Nelibur.ObjectMapper.Core.Extensions;
using Nelibur.ObjectMapper.Mappers.Caches;
using Nelibur.ObjectMapper.Mappers.Classes.Members;
using Nelibur.ObjectMapper.Mappers.MappingMembers;
using Nelibur.ObjectMapper.Reflection;

namespace Nelibur.ObjectMapper.Mappers.Classes
{
    internal sealed class ClassMapperBuilder : MapperBuilder
    {
        private const string CreateTargetInstanceMethod = "CreateTargetInstance";
        private const string MapClassMethod = "MapClass";
        private readonly IDynamicAssembly _assembly;
        private readonly MemberMapper _memberMapper;

        public ClassMapperBuilder(IDynamicAssembly assembly, IMapperBuilderSelector mapperBuilderSelector)
        {
            _assembly = assembly;
            _memberMapper = new MemberMapper(mapperBuilderSelector);
        }

        protected override string ScopeName
        {
            get { return "ClassMappers"; }
        }

        public Mapper Create(TypePair typePair)
        {
            Type parentType = typeof(ClassMapper<,>).MakeGenericType(typePair.Source, typePair.Target);
            TypeBuilder typeBuilder = _assembly.DefineType(GetMapperFullName(), parentType);
            EmitCreateTargetInstance(typePair.Target, typeBuilder);
            Option<MapperCache> mappers = EmitMapClass(typePair, typeBuilder);

            var result = (Mapper)Activator.CreateInstance(typeBuilder.CreateType());
            mappers.Do(x => result.AddMappers(x.Mappers));
            return result;
        }

        protected override bool IsSupportedCore(TypePair typePair)
        {
            return true;
        }

        private static void EmitCreateTargetInstance(Type targetType, TypeBuilder typeBuilder)
        {
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(CreateTargetInstanceMethod, OverrideProtected, targetType, Type.EmptyTypes);
            var codeGenerator = new CodeGenerator(methodBuilder.GetILGenerator());

            IEmitterType result = targetType.IsValueType ? EmitValueType(targetType, codeGenerator) : EmitRefType(targetType);

            EmitReturn.Return(result, targetType).Emit(codeGenerator);
        }

        private static IEmitterType EmitRefType(Type type)
        {
            return type.HasDefaultCtor() ? EmitNewObj.NewObj(type) : EmitNull.Load();
        }

        private static IEmitterType EmitValueType(Type type, CodeGenerator codeGenerator)
        {
            LocalBuilder builder = codeGenerator.DeclareLocal(type);
            EmitLocalVariable.Declare(builder).Emit(codeGenerator);
            return EmitBox.Box(EmitLocal.Load(builder));
        }

        private Option<MapperCache> EmitMapClass(TypePair typePair, TypeBuilder typeBuilder)
        {
            List<MappingMember> members = MappingMemberBuilder.Build(typePair);

            MethodBuilder methodBuilder = typeBuilder.DefineMethod(MapClassMethod, OverrideProtected, typePair.Target,
                new[] { typePair.Source, typePair.Target });
            var codeGenerator = new CodeGenerator(methodBuilder.GetILGenerator());

            var emitterComposite = new EmitComposite();

            MemberEmitterDescription emitterDescription = EmitMappingMembers(members);

            emitterComposite.Add(emitterDescription.Emitter);
            emitterComposite.Add(EmitReturn.Return(EmitArgument.Load(typePair.Target, 2)));
            emitterComposite.Emit(codeGenerator);
            return emitterDescription.MapperCache;
        }

        private MemberEmitterDescription EmitMappingMembers(List<MappingMember> members)
        {
            MemberEmitterDescription result = _memberMapper.Build(members);
            return result;
        }
    }
}