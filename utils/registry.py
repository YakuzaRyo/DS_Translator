"""
命令注册器模块
use for function registration
这个模块实现了一个命令注册系统，允许通过装饰器将函数注册到全局注册表中。
主要用于实现命令模式，方便管理和调用不同类型的命令。

使用示例:
    @register_commond("database", "find_oracle")
    def find_oracle():
        # 执行查找 Oracle 的逻辑
        pass
    
    # 获取所有注册的命令
    registry = get_registry()
    for group, name, func in registry:
        if group == "database" and name == "find_oracle":
            func()  # 调用注册的函数
"""

from functools import wraps
from typing import Callable, Any

# 全局注册表，存储所有注册的命令
# 格式: [(group, name, function), ...]
# group: 命令组名（如 "database", "file" 等）
# name: 命令名称（如 "find_oracle", "find_kingbase" 等）
# function: 注册的函数对象
_registry: list[tuple[str,str,Callable[...,None]]] = []


def register_commond(group:str,name:str)->Callable[...,None]:
    """
    命令注册装饰器
    
    将函数注册到全局注册表中，使其可以通过组名和命令名进行查找和调用。
    
    参数:
        group: 命令所属的组名，用于分类管理（如 "database", "file"）
        name: 命令的名称，用于标识具体的命令（如 "find_oracle"）
    
    返回:
        装饰器函数
    
    使用示例:
        @register_commond("database", "find_oracle")
        def my_function():
            print("查找 Oracle 数据库")
    """
    def decorator(func:Callable[...,None]) -> Callable[...,None]:
        """
        内部装饰器，实际执行注册逻辑
        
        参数:
            func: 要被注册的函数
        
        返回:
            包装后的函数（保持原函数的所有属性和行为）
        """
        @wraps(func)  # 保留原函数的元数据（函数名、文档字符串等）
        def wrapper(*args:Any,**kwargs:Any) -> Any:
            """
            包装函数，直接调用原函数，不做额外处理
            
            参数:
                *args: 位置参数
                **kwargs: 关键字参数
            
            返回:
                原函数的返回值
            """
            return func(*args,**kwargs)
        
        # 将命令注册到全局注册表
        # 存储格式: (组名, 命令名, 包装后的函数)
        _registry.append((group,name,wrapper))
        return wrapper
    return decorator


def get_registry()->list[tuple[str,str,Callable[...,None]]]:
    """
    获取命令注册表的副本
    
    返回注册表中所有已注册的命令信息，包括组名、命令名和对应的函数对象。
    返回的是副本，防止外部代码直接修改原始注册表。
    
    返回:
        注册表列表的副本，格式为 [(group, name, function), ...]
    
    使用示例:
        registry = get_registry()
        for group, name, func in registry:
            print(f"组: {group}, 命令: {name}")
            func()  # 调用注册的函数
    """
    return _registry.copy()